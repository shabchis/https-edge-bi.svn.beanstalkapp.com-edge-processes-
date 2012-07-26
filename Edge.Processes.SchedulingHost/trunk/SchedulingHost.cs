using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using Edge.Core.Scheduling;
using Edge.Core.Scheduling.Objects;
using Legacy = Edge.Core.Services;
using Edge.Core.Utilities;
using System.Data.SqlClient;
using Edge.Core.Configuration;
using System.Threading;
using Newtonsoft.Json;


namespace Edge.Processes.SchedulingHost
{
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
	public class SchedulingHost : ISchedulingHost
	{
		private const string ClassName = "SchedulingHost";
		private Scheduler _scheduler;
		private Listener _listener;
		private List<ISchedulingHostSubscriber> _callBacks = new List<ISchedulingHostSubscriber>();
		private Dictionary<Guid, ServiceInstanceInfo> _instancesEvents = new Dictionary<Guid, ServiceInstanceInfo>();
		//private Dictionary<Guid, Edge.Core.Scheduling.Objects.ServiceInstanceInfo> _scheduledServices = new Dictionary<Guid, Edge.Core.Scheduling.Objects.ServiceInstanceInfo>();

		#region General Methods
		//=================================================

		public void Init()
		{
			_scheduler = new Scheduler(true);
			_scheduler.ServiceRunRequiredEvent += new EventHandler<ServicesToRunEventArgs>(_scheduler_ServiceRunRequiredEvent);
			_scheduler.NewScheduleCreatedEvent += new EventHandler<SchedulingInformationEventArgs>(_scheduler_NewScheduleCreatedEvent);
			_listener = new Listener(_scheduler);
			_listener.Start();


			Thread t = new Thread(delegate()
			{
				while (true)
				{
					Thread.Sleep(3000);

					List<ServiceInstanceInfo> instances;
					lock (_instancesEvents)
					{
						instances = _instancesEvents.Values.ToList();
						_instancesEvents.Clear();
					}
					foreach (var callBack in _callBacks)
					{
						try
						{
							callBack.InstancesEvents(instances);
						}
						catch (Exception ex)
						{
							Log.Write(ClassName, ex.Message, ex, LogMessageType.Warning);
						}
					}

				}
			});
		}



		public void Stop()
		{
			_scheduler.Stop();
		}

		public void Start()
		{
			_scheduler.Start();
		}

		private Legacy.ServiceInstance GetLegacyInstanceByGuid(Guid guid)
		{
			var instance = _scheduler.ScheduledServices.Where(i => i.LegacyInstance.Guid == guid); //Get from legacyInstance
			if (instance.Count() > 0)
				return instance.ToList()[0].LegacyInstance;
			else
				throw new Exception(string.Format("Instance with guid {0} not found!", guid));
		}


		//=================================================
		#endregion


		#region WCF
		//=================================================

		public void Subscribe()
		{
			lock (_callBacks)
			{
				_callBacks.Add(OperationContext.Current.GetCallbackChannel<ISchedulingHostSubscriber>());
			}
		}

		public void Unsubscribe()
		{
			lock (_callBacks)
			{
				if (_callBacks != null && _callBacks.Contains(OperationContext.Current.GetCallbackChannel<ISchedulingHostSubscriber>()))
					_callBacks.Remove(OperationContext.Current.GetCallbackChannel<ISchedulingHostSubscriber>());
			}
		}

		public Legacy.PingInfo Ping(Guid guid)
		{
			Legacy.PingInfo alive;
			try
			{
				var instance = _scheduler.ScheduledServices.Where(i => i.LegacyInstance.Guid == guid); //Get from legacyInstance
				if (instance.Count() > 0)
					alive = instance.ToList()[0].LegacyInstance.Ping();
				else
				{
					alive = new Legacy.PingInfo();
					alive.Exception = new Exception(string.Format("Service with Guid {0} not found", guid));
				}
			}
			catch (Exception ex)
			{
				alive = new Legacy.PingInfo() { Timestamp = DateTime.Now, Exception = ex };
			}
			return alive;
		}


		public void Abort(Guid guid)
		{
			Legacy.ServiceInstance instance = null;
			try
			{
				instance = GetLegacyInstanceByGuid(guid);
				instance.Abort();
			}
			catch (Exception ex)
			{
				Log.Write(ClassName, ex.Message, ex, LogMessageType.Warning);
			}

		}

		public void ResetUnended()
		{
			using (SqlConnection SqlConnection = new SqlConnection(AppSettings.GetConnectionString(this, "System")))
			{
				SqlConnection.Open();
				using (SqlCommand sqlCommand = new SqlCommand("ResetUnendedServices", SqlConnection))
				{
					sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;
					sqlCommand.ExecuteNonQuery();


				}
			}
		}

		public ProfileInfo[] GetSchedulingProfiles()
		{
			throw new NotImplementedException();
			//return _scheduler.Profiles.Values.ToArray();
		}



		public Guid AddUnplannedService(int accountID, string serviceName, DateTime targetDateTime, Dictionary<string, string> options)
		{

			Guid guid;
			AccountElement accountElement = EdgeServicesConfiguration.Current.Accounts.GetAccount(accountID);
			if (accountElement == null)
				throw new Exception(String.Format("Account '{0}' not found in configuration.", accountID));

			Profile profile = new Profile()
			{
				ID = accountElement.ID,
				Name = accountElement.Name,
				Settings = new Dictionary<string, object>()
			};
			profile.Settings.Add("AccountID", accountElement.ID.ToString());
			AccountServiceElement accountServiceElement = accountElement.Services[serviceName];
			if (accountServiceElement == null)
				throw new Exception(String.Format("Service '{0}' not found in account {1}.", serviceName, accountID));

			//ActiveServiceElement activeServiceElement = new ActiveServiceElement(accountServiceElement);
			ServiceConfiguration myServiceConfiguration = ServiceConfiguration.FromLegacyConfiguration(accountServiceElement, _scheduler.GetServiceBaseConfiguration(accountServiceElement.Uses.Element.Name), profile);

			myServiceConfiguration.SchedulingRules.Add(SchedulingRule.CreateUnplanned());

			guid = myServiceConfiguration.SchedulingRules[0].GuidForUnplanned;
			myServiceConfiguration.SchedulingRules[0].Times.Add(new TimeSpan(0, 0, 0, 0));


			myServiceConfiguration.Profile = profile;

			_scheduler.AddServiceToSchedule(myServiceConfiguration);
			return guid;
		}

		//=================================================
		#endregion

		#region Event handlers
		//=================================================

		void _scheduler_NewScheduleCreatedEvent(object sender, SchedulingInformationEventArgs e)
		{
			List<Edge.Core.Scheduling.Objects.ServiceInstance> instances = e.ScheduleInformation.Values.ToList<Edge.Core.Scheduling.Objects.ServiceInstance>();
			AddToInstanceEvents(instances);
		}
		private void AddToInstanceEvents(ServiceInstance instance)
		{
			if (!_instancesEvents.ContainsKey(instance.LegacyInstance.Guid))
				_instancesEvents.Add(instance.LegacyInstance.Guid, instance.GetInfo());
			else
				_instancesEvents[instance.LegacyInstance.Guid] = instance.GetInfo();
		}

		private void AddToInstanceEvents(List<ServiceInstance> instances)
		{
			foreach (var instance in instances)
			{
				if (!_instancesEvents.ContainsKey(instance.LegacyInstance.Guid))
					_instancesEvents.Add(instance.LegacyInstance.Guid, instance.GetInfo());
				else
					_instancesEvents[instance.LegacyInstance.Guid] = instance.GetInfo();
			}
		}
		void _scheduler_ServiceRunRequiredEvent(object sender, EventArgs e)
		{
			ServicesToRunEventArgs args = (ServicesToRunEventArgs)e;
			foreach (Edge.Core.Scheduling.Objects.ServiceInstance serviceInstance in args.ServicesToRun)
			{
				serviceInstance.LegacyInstance.StateChanged += new EventHandler<Core.Services.ServiceStateChangedEventArgs>(LegacyInstance_StateChanged);
				serviceInstance.LegacyInstance.OutcomeReported += new EventHandler(LegacyInstance_OutcomeReported);
				serviceInstance.LegacyInstance.ChildServiceRequested += new EventHandler<Core.Services.ServiceRequestedEventArgs>(LegacyInstance_ChildServiceRequested);
				serviceInstance.LegacyInstance.ProgressReported += new EventHandler(LegacyInstance_ProgressReported);
				serviceInstance.LegacyInstance.Initialize();
			}
		}


		void LegacyInstance_ProgressReported(object sender, EventArgs e)
		{
			Legacy.ServiceInstance serviceInstance = (Edge.Core.Services.ServiceInstance)sender;
			double progress = serviceInstance.Progress * 100;
			//if (_scheduledServices.ContainsKey(instance.Guid))
			if (_scheduler.ScheduledServices.ContainsKey(serviceInstance.Guid))
			{
				Edge.Core.Scheduling.Objects.ServiceInstance instance = _scheduler.ScheduledServices[serviceInstance.Guid];
				AddToInstanceEvents(instance);
			}
		}


		void LegacyInstance_ChildServiceRequested(object sender, Core.Services.ServiceRequestedEventArgs e)
		{
			try
			{
				_scheduler.AddChildServiceToSchedule(e.RequestedService);
			}
			catch (Exception ex)
			{
				Edge.Core.Utilities.Log.Write(ClassName, ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}
		}
		void LegacyInstance_StateChanged(object sender, Core.Services.ServiceStateChangedEventArgs e)
		{
			try
			{
				Legacy.ServiceInstance serviceInstance = (Edge.Core.Services.ServiceInstance)sender;
				if (_scheduler.ScheduledServices.ContainsKey(serviceInstance.Guid))
				{

					Edge.Core.Scheduling.Objects.ServiceInstance instance = _scheduler.ScheduledServices[serviceInstance.Guid];
					if (serviceInstance.State == Legacy.ServiceState.Ready)
						serviceInstance.Start();
					AddToInstanceEvents(instance);
				}
				else
					throw new Exception("lo agioni");
			}
			catch (Exception ex)
			{
				Edge.Core.Utilities.Log.Write(ClassName, ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}
		}
		void LegacyInstance_OutcomeReported(object sender, EventArgs e)
		{
			Legacy.ServiceInstance serviceInstance = (Edge.Core.Services.ServiceInstance)sender;

			if (_scheduler.ScheduledServices.ContainsKey(serviceInstance.Guid))
			{
				Edge.Core.Scheduling.Objects.ServiceInstance Outcomeinstance = _scheduler.ScheduledServices[serviceInstance.Guid];
				AddToInstanceEvents(Outcomeinstance);

			}
			else
				throw new Exception("LO agioni");
		}
		//=================================================
		#endregion






	}


}
