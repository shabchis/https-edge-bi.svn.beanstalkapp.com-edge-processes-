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
		private Scheduler _scheduler;
		private Listener _listener;
		private List<ISchedulingHostSubscriber> _callBacks = new List<ISchedulingHostSubscriber>();
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
					instance =_scheduler.ScheduledServices.Where(i => i.SchedulingRequest.Guid == guid); //Get from scheduling guid
					if (instance.Count() > 0)
						alive = instance.ToList()[0].LegacyInstance.Ping();
					else //finished so take from history
					{
						alive = new Legacy.PingInfo() { Timestamp = DateTime.Now };
						var item = _scheduler.SchedulerState.HistoryItems.Where(h => h.Value.Guid == guid);
						if (item.Count() > 0)
						{
							HistoryItem historyItem = item.ToList()[0].Value;
							alive.InstanceGuid = historyItem.Guid;
						}
						else
							alive.Exception = new Exception (string.Format("Service with Guid {0} not found", guid));
					}
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
				Log.Write(this.ToString(), ex.Message, ex, LogMessageType.Warning);
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

		public List<AccountServiceInformation> GetServicesConfigurations()
		{
			List<AccountServiceInformation> accounsServiceInformation = new List<AccountServiceInformation>();
			foreach (AccountElement account in EdgeServicesConfiguration.Current.Accounts)
			{
				AccountServiceInformation accounServiceInformation;
				accounServiceInformation = new AccountServiceInformation() { AccountName = account.Name, ID = account.ID };
				accounServiceInformation.Services = new List<string>();
				foreach (AccountServiceElement service in account.Services)
					accounServiceInformation.Services.Add(service.Name);
				accounsServiceInformation.Add(accounServiceInformation);
			}
			return accounsServiceInformation;

		}

		

		public Guid AddUnplannedService(int accountID, string serviceName, Dictionary<string, string> options, DateTime targetDateTime)
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
			Edge.Core.Scheduling.Objects.ServiceInstanceInfo[] instancesInfo = new Edge.Core.Scheduling.Objects.ServiceInstanceInfo[e.ScheduleInformation.Count];
			//int index = 0;
			//foreach (KeyValuePair<SchedulingRequest, Edge.Core.Scheduling.Objects.ServiceInstanceInfo> SchedInfo in e.ScheduleInformation)
			//{
			//    instancesInfo[index] = new Edge.Core.Scheduling.Objects.ServiceInstanceInfo()
			//    {
			//        LegacyInstanceGuid = SchedInfo.Value.LegacyInstance.Guid,
			//        AccountID = SchedInfo.Key.Configuration.Profile.ID,
			//        LegacyInstanceID = SchedInfo.Value.LegacyInstance.InstanceID.ToString(),
			//        LegacyOutcome = SchedInfo.Value.LegacyInstance.Outcome,
			//        ScheduleStartTime = SchedInfo.Value.ExpectedStartTime,
			//        ScheduleEndTime = SchedInfo.Value.ExpectedEndTime,
			//        BaseScheduleTime = SchedInfo.Key.RequestedTime,
			//        LegacyActualStartTime = SchedInfo.Value.LegacyInstance.TimeStarted,
			//        LegacyActualEndTime = SchedInfo.Value.LegacyInstance.TimeEnded,
			//        ServiceName = SchedInfo.Value.Configuration.Name,
			//        LegacyState = SchedInfo.Value.LegacyInstance.State,
			//        //ScheduledID = SchedInfo.Value.ScheduledID,
			//        Options = JsonConvert.SerializeObject(SchedInfo.Value.LegacyInstance.Configuration.Options.Definition),
			//        LegacyParentInstanceID = SchedInfo.Value.LegacyInstance.ParentInstance != null ? SchedInfo.Value.LegacyInstance.ParentInstance.Guid : Guid.Empty,
			//        LegacyProgress = SchedInfo.Value.LegacyInstance.State == Legacy.ServiceState.Ended ? 100 : SchedInfo.Value.LegacyInstance.Progress
			//    };
			//    _scheduledServices[SchedInfo.Value.LegacyInstance.Guid] = instancesInfo[index];
			//    index++;
			//}
			if (_callBacks != null)
			{
				foreach (var callBack in _callBacks)
				{
					try
					{
						callBack.ScheduleCreated(instancesInfo);
					}
					catch (Exception ex)
					{
						Log.Write("SchedulingHost", ex.Message, ex, LogMessageType.Warning);
					}
				}
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
			Legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
			double progress = instance.Progress * 100;
			//if (_scheduledServices.ContainsKey(instance.Guid))
			if (_scheduler.ScheduledServices.ContainsKey(instance.Guid))
			{
				Edge.Core.Scheduling.Objects.ServiceInstanceInfo instanceInfo =_scheduler.ScheduledServices[instance.Guid].GetInfo();
				instanceInfo.LegacyProgress = progress;
				foreach (var callBack in _callBacks)
				{
					try
					{
						callBack.InstanceEvent(instanceInfo);
					}
					catch (Exception ex)
					{
						Log.Write("SchedulingHost", ex.Message, ex, LogMessageType.Warning);
					}
				}
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
				Edge.Core.Utilities.Log.Write("SchedulingControlForm", ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}
		}
		void LegacyInstance_StateChanged(object sender, Core.Services.ServiceStateChangedEventArgs e)
		{
			try
			{
				Legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
				
				

				if (_scheduler.ScheduledServices.ContainsKey(instance.Guid))
				{

					Edge.Core.Scheduling.Objects.ServiceInstanceInfo stateInfo = _scheduler.ScheduledServices[instance.Guid].GetInfo();
					stateInfo.LegacyState = instance.State;
					
					if (instance.State==Legacy.ServiceState.Ready)
					{
												
						Log.Write(instance.GetType().ToString(), string.Format("instance {0} with id {1} registered outcomereported", instance.Configuration.Name, instance.InstanceID), LogMessageType.Information);
						stateInfo.LegacyActualStartTime = instance.TimeStarted;
						instance.Start();
					}
					foreach (var callBack in _callBacks)
					{
						try
						{
							callBack.InstanceEvent(stateInfo);
						}
						catch (Exception ex)
						{
							Log.Write("SchedulingHost", ex.Message, ex, LogMessageType.Warning);
						}
					}
				}
				else
					throw new Exception("lo agioni");


			}
			catch (Exception ex)
			{
				Edge.Core.Utilities.Log.Write("SchedulingControlForm", ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}
		}
		void LegacyInstance_OutcomeReported(object sender, EventArgs e)
		{
			Legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
			Log.Write(instance.GetType().ToString(), string.Format("instance {0} with id {1}  outcome reported", instance.Configuration.Name, instance.InstanceID), LogMessageType.Information);
			if (_scheduler.ScheduledServices.ContainsKey(instance.Guid))
			{
				Edge.Core.Scheduling.Objects.ServiceInstanceInfo OutcomeInfo = _scheduler.ScheduledServices[instance.Guid].GetInfo();
				OutcomeInfo.LegacyOutcome = instance.Outcome;
				OutcomeInfo.LegacyActualEndTime = instance.TimeEnded;
				OutcomeInfo.LegacyProgress = 100;
				foreach (var callBack in _callBacks)
				{
					try
					{
						callBack.InstanceEvent(OutcomeInfo);
					}
					catch (Exception ex)
					{
						Log.Write("SchedulingHost", ex.Message, ex, LogMessageType.Warning);
					}
				}
				_scheduler.CleandEndedUnplaned(instance);

				
			}
			else
				throw new Exception("LO agioni");
		}
		//=================================================
		#endregion






	}

}
