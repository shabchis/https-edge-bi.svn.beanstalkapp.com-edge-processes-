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
using Edge.Core.Services;
using System.Threading;


namespace Edge.Processes.SchedulingHost
{
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
	public class SchedulingHost : ISchedulingHost
	{
		private Scheduler _scheduler;
		private Listener _listener;
		private List<ISchedulingHostSubscriber> _callBacks = new List<ISchedulingHostSubscriber>();
		private Dictionary<Guid, Edge.Core.Scheduling.Objects.ServiceInstanceInfo> _scheduledServices = new Dictionary<Guid, Edge.Core.Scheduling.Objects.ServiceInstanceInfo>();

		#region Methods
		//=================================================

		public void Init()
		{
			_scheduler = new Scheduler(true);
			_scheduler.ServiceRunRequiredEvent += new EventHandler(_scheduler_ServiceRunRequiredEvent);
			_scheduler.NewScheduleCreatedEvent += new EventHandler(_scheduler_NewScheduleCreatedEvent);

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

		public void End()
		{
			_scheduler.Stop();
			_scheduler = null;
		}

		internal void Subscribe()
		{
			_callBacks.Add(OperationContext.Current.GetCallbackChannel<ISchedulingHostSubscriber>());
		}

		public Legacy.IsAlive GetStatus(string guid)
		{
			return IsAlive(Guid.Parse(guid));
		}

		public Legacy.IsAlive IsAlive(Guid guid)
		{
			try
			{
				return _scheduler.IsAlive(guid);

			}
			catch (Exception ex)
			{

				return new Legacy.IsAlive() { State = ex.Message };
			}
		}

		public void Abort(Guid guid)
		{
			Legacy.ServiceInstance instance = null;
			try
			{
				instance = _scheduler.GetInstance(guid);
				instance.Abort();
			}
			catch (Exception ex)
			{
				Log.Write(this.ToString(), ex.Message, ex, LogMessageType.Warning);
			}

		}

		public void ResetUnEnded()
		{
			_scheduler.RestUnEnded();
		}

		public List<AccountServiceInformation> GetServicesConfigurations()
		{
			return _scheduler.GetServicesConfigurations();
		}

		public Guid AddUnplanedService(int accountID, string serviceName, Dictionary<string, string> options, DateTime targetDateTime)
		{

			Guid guid;
			AccountElement accountElement = EdgeServicesConfiguration.Current.Accounts.GetAccount(accountID);
			if (accountElement == null)
				throw new Exception(String.Format("Account '{0}' not found in configuration.", accountID));

			AccountServiceElement accountServiceElement = accountElement.Services[serviceName];
			if (accountServiceElement == null)
				throw new Exception(String.Format("Service '{0}' not found in account {1}.", serviceName, accountID));

			ActiveServiceElement activeServiceElement = new ActiveServiceElement(accountServiceElement);
			ServiceConfiguration myServiceConfiguration = ServiceConfiguration.FromLegacy(activeServiceElement, options);

			myServiceConfiguration.SchedulingRules.Add(new SchedulingRule()
			{
				Scope = SchedulingScope.Unplanned,
				SpecificDateTime = targetDateTime,
				MaxDeviationAfter = new TimeSpan(0, 0, 120, 0, 0),
				Times = new List<TimeSpan>(),
				GuidForUnplaned = Guid.NewGuid()
			});

			guid = myServiceConfiguration.SchedulingRules[0].GuidForUnplaned;
			myServiceConfiguration.SchedulingRules[0].Times.Add(new TimeSpan(0, 0, 0, 0));
			
			Profile profile = new Profile()
			{
				ID = accountElement.ID,
				Name = accountElement.Name,
				Settings = new Dictionary<string, object>()
			};
			profile.Settings.Add("AccountID", accountElement.ID.ToString());
			myServiceConfiguration.SchedulingProfile = profile;

			_scheduler.AddNewServiceToSchedule(myServiceConfiguration);
			return guid;
		}

		//=================================================
		#endregion

		#region Events
		//=================================================

		void _scheduler_NewScheduleCreatedEvent(object sender, EventArgs e)
		{
			ScheduledInformationEventArgs ee = (ScheduledInformationEventArgs)e;
			Edge.Core.Scheduling.Objects.ServiceInstanceInfo[] instancesInfo = new Edge.Core.Scheduling.Objects.ServiceInstanceInfo[ee.ScheduleInformation.Count];
			int index = 0;
			foreach (KeyValuePair<SchedulingData, Edge.Core.Scheduling.Objects.ServiceInstance> SchedInfo in ee.ScheduleInformation)
			{
				string date;
				if (SchedInfo.Value.LegacyInstance.Configuration.Options.ContainsKey("Date"))
					date = SchedInfo.Value.LegacyInstance.Configuration.Options["Date"];
				else if (SchedInfo.Value.LegacyInstance.Configuration.Options.ContainsKey("TargetPeriod"))
					date = SchedInfo.Value.LegacyInstance.Configuration.Options["TargetPeriod"];
				else
					date = string.Empty;
				instancesInfo[index] = new Edge.Core.Scheduling.Objects.ServiceInstanceInfo()
				{
					LegacyInstanceGuid = SchedInfo.Value.LegacyInstance.Guid,
					AccountID = SchedInfo.Key.ProfileID,
					TargetPeriod = date,
					InstanceID = SchedInfo.Value.LegacyInstance.InstanceID.ToString(),
					Outcome = SchedInfo.Value.LegacyInstance.Outcome,
					SchdeuleStartTime = SchedInfo.Value.StartTime,
					ScheduleEndTime = SchedInfo.Value.EndTime,
					BaseScheduleTime = SchedInfo.Key.TimeToRun,
					ActualStartTime = SchedInfo.Value.LegacyInstance.TimeStarted,
					ActualEndTime = SchedInfo.Value.LegacyInstance.TimeEnded,
					ServiceName = SchedInfo.Value.ServiceName,
					State = SchedInfo.Value.LegacyInstance.State,
					ScheduledID = SchedInfo.Value.ScheduledID,
					ParentInstanceID = SchedInfo.Value.LegacyInstance.ParentInstance != null ? SchedInfo.Value.LegacyInstance.ParentInstance.Guid : Guid.Empty,
					Progress = SchedInfo.Value.LegacyInstance.State == Legacy.ServiceState.Ended ? 100 : SchedInfo.Value.LegacyInstance.Progress
				};
				_scheduledServices[SchedInfo.Value.LegacyInstance.Guid] = instancesInfo[index];
				index++;
			}
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
			TimeToRunEventArgs args = (TimeToRunEventArgs)e;
			foreach (Edge.Core.Scheduling.Objects.ServiceInstance serviceInstance in args.ServicesToRun)
			{
				serviceInstance.LegacyInstance.StateChanged += new EventHandler<Core.Services.ServiceStateChangedEventArgs>(LegacyInstance_StateChanged);
				serviceInstance.LegacyInstance.ChildServiceRequested += new EventHandler<Core.Services.ServiceRequestedEventArgs>(LegacyInstance_ChildServiceRequested);
				serviceInstance.LegacyInstance.ProgressReported += new EventHandler(LegacyInstance_ProgressReported);
				serviceInstance.LegacyInstance.OutcomeReported += new EventHandler(instance_OutcomeReported);
				serviceInstance.LegacyInstance.Initialize();
			}
		}
		void LegacyInstance_ProgressReported(object sender, EventArgs e)
		{
			Legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
			double progress = instance.Progress * 100;
			if (_scheduledServices.ContainsKey(instance.Guid))
			{
				Edge.Core.Scheduling.Objects.ServiceInstanceInfo instanceInfo = _scheduledServices[instance.Guid];
				instanceInfo.Progress = progress;
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
				Legacy.ServiceInstance instance = (Legacy.ServiceInstance)sender;
				e.RequestedService.ChildServiceRequested += new EventHandler<Legacy.ServiceRequestedEventArgs>(LegacyInstance_ChildServiceRequested);
				e.RequestedService.StateChanged += new EventHandler<Legacy.ServiceStateChangedEventArgs>(LegacyInstance_StateChanged);
				e.RequestedService.ProgressReported += new EventHandler(LegacyInstance_ProgressReported);
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
				if (e.StateAfter == Legacy.ServiceState.Ready)
					instance.Start();
				if (_scheduledServices.ContainsKey(instance.Guid))
				{

					Edge.Core.Scheduling.Objects.ServiceInstanceInfo stateInfo = _scheduledServices[instance.Guid];
					stateInfo.State = e.StateAfter;
					if (e.StateAfter == Legacy.ServiceState.Ready)
						stateInfo.ActualStartTime = instance.TimeStarted;
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
		void instance_OutcomeReported(object sender, EventArgs e)
		{
			Legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
			if (_scheduledServices.ContainsKey(instance.Guid))
			{
				Edge.Core.Scheduling.Objects.ServiceInstanceInfo OutcomeInfo = _scheduledServices[instance.Guid];
				OutcomeInfo.Outcome = instance.Outcome;
				OutcomeInfo.ActualEndTime = instance.TimeEnded;
				OutcomeInfo.Progress = 100;
				foreach (var callBack in _callBacks)
					try
					{
						callBack.InstanceEvent(OutcomeInfo);
					}
					catch (Exception ex)
					{
						Log.Write("SchedulingHost", ex.Message, ex, LogMessageType.Warning);
					}

				_scheduler.Schedule(true);
			}
			else
				throw new Exception("LO agioni");
		}
		//=================================================
		#endregion
	}
	
}
