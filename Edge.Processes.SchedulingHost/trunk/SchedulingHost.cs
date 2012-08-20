using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using Edge.Core.Scheduling;
using Edge.Core.Services;
using Edge.Core.Utilities;
using System.Data.SqlClient;
using Edge.Core.Configuration;
using System.Configuration;
using System.Threading;
using Newtonsoft.Json;
using Edge.Core.Services.Configuration;

namespace Edge.Processes.SchedulingHost
{
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
	public class SchedulingHost : ISchedulingHost
	{
		private const string ClassName = "SchedulingHost";
		private Scheduler _scheduler;
		//private Listener _listener;
		private List<ISchedulingHostSubscriber> _callBacks = new List<ISchedulingHostSubscriber>();
		private Dictionary<Guid, ServiceInstance> _requestsEvents = new Dictionary<Guid, ServiceInstance>();
		//private Dictionary<Guid, Edge.Core.Scheduling.Objects.ServiceInstanceInfo> _scheduledServices = new Dictionary<Guid, Edge.Core.Scheduling.Objects.ServiceInstanceInfo>();

		#region General Methods
		//=================================================

		public void Init()
		{
			_scheduler = new Scheduler(true);
			_scheduler.ScheduledRequestTimeArrived += new EventHandler<SchedulingRequestTimeArrivedArgs>(_scheduler_ServiceRunRequiredEvent);
			_scheduler.NewScheduleCreatedEvent += new EventHandler<SchedulingInformationEventArgs>(_scheduler_NewScheduleCreatedEvent);
			//_listener = new Listener(_scheduler,this);
			//_listener.Start();


			Thread t = new Thread(delegate()
			{
				while (true)
				{
					Thread.Sleep(3000);

					List<ServiceInstance> instances;
					lock (_requestsEvents)
					{
						instances = _requestsEvents.Values.ToList();
						_requestsEvents.Clear();
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

		private ServiceInstance GetLegacyInstanceByGuid(Guid guid)
		{
			var instance = _scheduler.ScheduledServices.Where(i => i.InstanceID == guid); //Get from legacyInstance
			if (instance.Count() > 0)
				return instance.ToList()[0];
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

		public PingInfo Ping(Guid guid)
		{
			PingInfo alive;
			try
			{
				var instance = _scheduler.ScheduledServices.Where(i => i.InstanceID == guid); //Get from legacyInstance
				if (instance.Count() > 0)
					alive = instance.ToList()[0].Ping();
				else
				{
					alive = new PingInfo();
					alive.Exception = new Exception(string.Format("Service with Guid {0} not found", guid));
				}
			}
			catch (Exception ex)
			{
				alive = new PingInfo() { Timestamp = DateTime.Now, Exception = ex };
			}
			return alive;
		}


		public void Abort(Guid guid)
		{
			ServiceInstance instance = null;
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

		public ProfilesCollection GetSchedulingProfiles()
		{
			return _scheduler.Profiles;
		}



		public Guid AddUnplannedService(int accountID, string serviceName, DateTime targetDateTime, Dictionary<string, string> options)
		{
			AccountElement accountElement = EdgeServicesConfiguration.Current.Accounts.GetAccount(accountID);
			if (accountElement == null)
				throw new Exception(String.Format("Account '{0}' not found in configuration.", accountID));

			ServiceProfile profile;
			if (!_scheduler.Profiles.TryGetValue(accountID, out profile))
				throw new Exception("No profile found for account " + accountID.ToString());

			AccountServiceElement accountServiceElement = accountElement.Services[serviceName];
			if (accountServiceElement == null)
				throw new Exception(String.Format("Service '{0}' not found in account {1}.", serviceName, accountID));

			
			ServiceConfiguration myServiceConfiguration =new  ServiceConfiguration.FromLegacyConfiguration(accountServiceElement, _scheduler.GetServiceBaseConfiguration(accountServiceElement.Uses.Element.Name), profile,options);
			myServiceConfiguration.Profile = profile;

			ServiceInstance request =new ServiceInstance();
			SchedulingRule rule = new SchedulingRule()
				{
					Scope = SchedulingScope.Unplanned,
					SpecificDateTime = targetDateTime,
					MaxDeviationAfter = TimeSpan.FromHours(1)
				};
				
			

			_scheduler.AddRequestToSchedule(request);
			return request.InstanceID;
		}

		//=================================================
		#endregion

		#region Event handlers
		//=================================================

		void _scheduler_NewScheduleCreatedEvent(object sender, SchedulingInformationEventArgs e)
		{
			AddToRequestsEvents(e.ScheduleInformation);
		}
		private void AddToRequestsEvents(ServiceInstance request)
		{
			if (!_requestsEvents.ContainsKey(request.InstanceID))
				_requestsEvents.Add(request.InstanceID, request);
			else
				_requestsEvents[request.InstanceID] = request;
		}

		private void AddToRequestsEvents(List<ServiceInstance> requests)
		{
			foreach (var request in requests)
			{
				if (!_requestsEvents.ContainsKey(request.InstanceID))
					_requestsEvents.Add(request.InstanceID, request);
				else
					_requestsEvents[request.InstanceID] = request;
			}
		}
		void _scheduler_ServiceRunRequiredEvent(object sender, SchedulingRequestTimeArrivedArgs e)
		{
			e.Request.StateChanged += new EventHandler(Instance_StateChanged); 
			e.Request.OutcomeReported += new EventHandler(Instance_OutcomeReported);
			//e.Request.ChildServiceRequested += new EventHandler<Legacy.ServiceRequestedEventArgs>(Instance_ChildServiceRequested);
			e.Request.ProgressReported += new EventHandler(Instance_ProgressReported);
			e.Request.Initialize();
		}

		void Instance_ProgressReported(object sender, EventArgs e)
		{
			ServiceInstance serviceInstance = (ServiceInstance)sender;
			ServiceInstance request = serviceInstance;
			double progress = serviceInstance.Progress * 100;
			AddToRequestsEvents(request);

		}

		//void Instance_ChildServiceRequested(object sender, Legacy.ServiceRequestedEventArgs e)
		//{
		//    try
		//    {
		//        _scheduler.AddChildServiceToSchedule(e.RequestedService);
		//    }
		//    catch (Exception ex)
		//    {
		//        Edge.Core.Utilities.Log.Write(ClassName, ex.Message, ex, Edge.Core.Services.LogMessageType.Error);
		//    }
		//}

		void Instance_OutcomeReported(object sender, EventArgs e)
		{
			ServiceInstance serviceInstance = (ServiceInstance)sender;
			AddToRequestsEvents(serviceInstance);
		}

		void Instance_StateChanged(object sender, EventArgs e)
		{
			try
			{
				ServiceInstance serviceInstance = (ServiceInstance)sender;
				if (serviceInstance.State == ServiceState.Ready)
					serviceInstance.Start();
				AddToRequestsEvents(serviceInstance);
			}
			catch (Exception ex)
			{
				Edge.Core.Utilities.Log.Write(ClassName, ex.Message, ex, Edge.Core.Services.LogMessageType.Error);
			}
		}






		//=================================================
		#endregion






	}


}
