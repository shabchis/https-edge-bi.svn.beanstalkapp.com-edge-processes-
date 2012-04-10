using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using Edge.Core.Scheduling;
using Edge.Core.Scheduling.Objects;
using legacy = Edge.Core.Services;
using Edge.Core.Utilities;


namespace Edge.Processes.SchedulingHost
{
	[ServiceContract(SessionMode = SessionMode.Required,
	 CallbackContract = typeof(ICallBack))]
	public interface ISchedulingCommunication
	{
		[OperationContract]
		void Subscribe();
		[OperationContract]
		legacy.IsAlive IsAlive(Guid guid);
		[OperationContract]
		void Abort(Guid guid);
	}
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
	public class ScheulingCommunication : ISchedulingCommunication
	{
		private Scheduler _scheduler;
		private Listener _listener;
		private List<ICallBack> _callBacks = new List<ICallBack>();
		private Dictionary<Guid, ServiceInstanceInfo> _scheduledServices = new Dictionary<Guid, ServiceInstanceInfo>();

		#region events
		void _scheduler_NewScheduleCreatedEvent(object sender, EventArgs e)
		{
			ScheduledInformationEventArgs ee = (ScheduledInformationEventArgs)e;
			ServiceInstanceInfo[] instancesInfo = new ServiceInstanceInfo[ee.ScheduleInformation.Count];
			int index = 0;
			foreach (KeyValuePair<SchedulingData, ServiceInstance> SchedInfo in ee.ScheduleInformation)
			{
				string date;
				if (SchedInfo.Value.LegacyInstance.Configuration.Options.ContainsKey("Date"))
					date = SchedInfo.Value.LegacyInstance.Configuration.Options["Date"];
				else if (SchedInfo.Value.LegacyInstance.Configuration.Options.ContainsKey("TargetPeriod"))
					date = SchedInfo.Value.LegacyInstance.Configuration.Options["TargetPeriod"];
				else
					date = string.Empty;
				instancesInfo[index] = new ServiceInstanceInfo()
				{
					LegacyInstanceGuid = SchedInfo.Value.LegacyInstance.Guid,
					AccountID = SchedInfo.Key.profileID,
					DayCode = date,
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
					Progress = SchedInfo.Value.LegacyInstance.State == legacy.ServiceState.Ended ? 100 : SchedInfo.Value.LegacyInstance.Progress
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
						Log.Write(ex.Message, ex, LogMessageType.Warning);
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
			legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
			double progress = instance.Progress * 100;
			if (_scheduledServices.ContainsKey(instance.Guid))
			{
				ServiceInstanceInfo instanceInfo = _scheduledServices[instance.Guid];
				instanceInfo.Progress = progress;
				foreach (var callBack in _callBacks)
				{
					try
					{
						callBack.InstanceEvent(instanceInfo);
					}
					catch (Exception ex)
					{

						Log.Write(ex.Message, ex, LogMessageType.Warning);
					}
				}
			}
		}
		void LegacyInstance_ChildServiceRequested(object sender, Core.Services.ServiceRequestedEventArgs e)
		{
			try
			{
				legacy.ServiceInstance instance = (legacy.ServiceInstance)sender;
				e.RequestedService.ChildServiceRequested += new EventHandler<legacy.ServiceRequestedEventArgs>(LegacyInstance_ChildServiceRequested);
				e.RequestedService.StateChanged += new EventHandler<legacy.ServiceStateChangedEventArgs>(LegacyInstance_StateChanged);
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
				legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
				if (e.StateAfter == legacy.ServiceState.Ready)
					instance.Start();
				if (_scheduledServices.ContainsKey(instance.Guid))
				{

					ServiceInstanceInfo stateInfo = _scheduledServices[instance.Guid];
					stateInfo.State = e.StateAfter;
					if (e.StateAfter == legacy.ServiceState.Ready)
						stateInfo.ActualStartTime = instance.TimeStarted;
					foreach (var callBack in _callBacks)
					{
						try
						{
							callBack.InstanceEvent(stateInfo);
						}
						catch (Exception ex)
						{
							Log.Write(ex.Message, ex, LogMessageType.Warning);
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
			legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
			if (_scheduledServices.ContainsKey(instance.Guid))
			{
				ServiceInstanceInfo OutcomeInfo = _scheduledServices[instance.Guid];
				OutcomeInfo.Outcome = instance.Outcome;
				OutcomeInfo.ActualEndTime = instance.TimeEnded;
				OutcomeInfo.Progress = 100;
				foreach (var callBack in _callBacks)
					callBack.InstanceEvent(OutcomeInfo);
				_scheduler.Schedule(true);
			}
			else
				throw new Exception("LO agioni");
		}
		#endregion
		internal void Stop()
		{
			_scheduler.Stop();
		}
		internal void Start()
		{
			_scheduler.Start();
		}
		internal void End()
		{
			_scheduler.Stop();
			_scheduler = null;
		}
		internal void Init()
		{
			_scheduler = new Scheduler(true);
			_scheduler.ServiceRunRequiredEvent += new EventHandler(_scheduler_ServiceRunRequiredEvent);
			_scheduler.NewScheduleCreatedEvent += new EventHandler(_scheduler_NewScheduleCreatedEvent);
			_listener = new Listener(_scheduler);
			_listener.Start();
			_scheduler.Start();
		}
		public void Subscribe()
		{
			_callBacks.Add(OperationContext.Current.GetCallbackChannel<ICallBack>());
		}
		public legacy.IsAlive IsAlive(Guid guid)
		{
			try
			{
				legacy.ServiceInstance instance = _scheduler.GetInstance(guid);
				return instance.IsAlive();
			}
			catch (Exception ex)
			{

				return new legacy.IsAlive() { OutCome = ex.Message };
			}


		}
		public void Abort(Guid guid)
		{
			legacy.ServiceInstance instance = null;
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
	}	
	public interface ICallBack
	{
		[OperationContract(IsOneWay = true)]
		void ScheduleCreated(ServiceInstanceInfo[] scheduleAndStateInfo);

		[OperationContract(IsOneWay = true)]
		void InstanceEvent(ServiceInstanceInfo StateOutcomerInfo);
	}
}
