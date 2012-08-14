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
			_scheduler.ScheduledRequestTimeArrived += new EventHandler<SchedulingRequestTimeArrivedArgs>(_scheduler_ServiceRunRequiredEvent);
			_scheduler.NewScheduleCreatedEvent += new EventHandler<SchedulingInformationEventArgs>(_scheduler_NewScheduleCreatedEvent);
			_listener = new Listener(_scheduler,this);
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
							if (instances != null && instances.Count > 0)
								callBack.InstancesEvents(instances);
						}
						catch (Exception ex)
						{
							Log.Write(ClassName, ex.Message, ex, LogMessageType.Warning);
						}
					}

				}
			});
			t.Start();
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
			var instance = _scheduler.ScheduledServices.Where(i => i.Instance.LegacyInstance.Guid == guid); //Get from legacyInstance
			if (instance.Count() > 0)
				return instance.ToList()[0].Instance.LegacyInstance;
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
				var instance = _scheduler.ScheduledServices.Where(i => i.Instance.LegacyInstance.Guid == guid); //Get from legacyInstance
				if (instance.Count() > 0)
					alive = instance.ToList()[0].Instance.LegacyInstance.Ping();
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
			return _scheduler.Profiles.GetProfilesInfo();
		}



		public Guid AddUnplannedService(int accountID, string serviceName, DateTime targetDateTime, Dictionary<string, string> options)
		{
			AccountElement accountElement = EdgeServicesConfiguration.Current.Accounts.GetAccount(accountID);
			if (accountElement == null)
				throw new Exception(String.Format("Account '{0}' not found in configuration.", accountID));

			Profile profile;
			if (!_scheduler.Profiles.TryGetValue(accountID, out profile))
				throw new Exception("No profile found for account " + accountID.ToString());

			AccountServiceElement accountServiceElement = accountElement.Services[serviceName];
			if (accountServiceElement == null)
				throw new Exception(String.Format("Service '{0}' not found in account {1}.", serviceName, accountID));

			
			ServiceConfiguration myServiceConfiguration = ServiceConfiguration.FromLegacyConfiguration(accountServiceElement, _scheduler.GetServiceBaseConfiguration(accountServiceElement.Uses.Element.Name), profile,options);
			myServiceConfiguration.Profile = profile;

			SchedulingRequest request = new SchedulingRequest(
				myServiceConfiguration,
				new SchedulingRule()
				{
					Scope = SchedulingScope.Unplanned,
					SpecificDateTime = targetDateTime,
					MaxDeviationAfter = TimeSpan.FromHours(1)
				},
				targetDateTime
			);

			_scheduler.AddRequestToSchedule(request);
			return request.RequestID;
		}

		//=================================================
		#endregion

		#region Event handlers
		//=================================================

		void _scheduler_NewScheduleCreatedEvent(object sender, SchedulingInformationEventArgs e)
		{
			List<ServiceInstance> instances = e.ScheduleInformation.ConvertAll<ServiceInstance>(p => p.Instance);
			if (instances != null && instances.Count > 0)
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
		void _scheduler_ServiceRunRequiredEvent(object sender, SchedulingRequestTimeArrivedArgs e)
		{
			e.Request.Instance.StateChanged += new EventHandler(Instance_StateChanged); 
			e.Request.Instance.OutcomeReported += new EventHandler(Instance_OutcomeReported);
			e.Request.Instance.ChildServiceRequested += new EventHandler<Legacy.ServiceRequestedEventArgs>(Instance_ChildServiceRequested);
			e.Request.Instance.ProgressReported += new EventHandler(Instance_ProgressReported);
			e.Request.Instance.Initialize();
		}

		void Instance_ProgressReported(object sender, EventArgs e)
		{
			ServiceInstance serviceInstance = (ServiceInstance)sender;
			double progress = serviceInstance.Progress * 100;
			AddToInstanceEvents(serviceInstance);

		}

		void Instance_ChildServiceRequested(object sender, Legacy.ServiceRequestedEventArgs e)
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

		void Instance_OutcomeReported(object sender, EventArgs e)
		{
			ServiceInstance serviceInstance = (ServiceInstance)sender;
			AddToInstanceEvents(serviceInstance);
		}

		void Instance_StateChanged(object sender, EventArgs e)
		{
			try
			{
				ServiceInstance serviceInstance = (ServiceInstance)sender;
				if (serviceInstance.State == Legacy.ServiceState.Ready)
					serviceInstance.Start();
				AddToInstanceEvents(serviceInstance);
			}
			catch (Exception ex)
			{
				Edge.Core.Utilities.Log.Write(ClassName, ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}
		}






		//=================================================
		#endregion






	}


}
