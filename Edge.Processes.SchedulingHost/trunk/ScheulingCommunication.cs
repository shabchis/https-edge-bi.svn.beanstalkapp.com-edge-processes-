using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using Edge.Core.Scheduling;
using Edge.Core.Scheduling.Objects;
using legacy = Edge.Core.Services;

namespace Edge.Processes.SchedulingHost
{
	[ServiceContract(SessionMode = SessionMode.Required,
	 CallbackContract = typeof(ICallBack))]
	public interface ISchedulingCommunication
	{

		[OperationContract]
		void Subscribe();

	}
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
	public class ScheulingCommunication : ISchedulingCommunication
	{
		private Scheduler _scheduler;
		private Listener _listener;
		private List<ICallBack> _callBacks = new List<ICallBack>();
		private Dictionary<int, ServiceInstanceInfo> _scheduledServices = new Dictionary<int, ServiceInstanceInfo>();
		#region IScheulingCommunication Members

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

		#endregion

		void _scheduler_NewScheduleCreatedEvent(object sender, EventArgs e)
		{
			ScheduledInformationEventArgs ee = (ScheduledInformationEventArgs)e;
			_scheduledServices.Clear();
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
					AccountID = SchedInfo.Key.profileID,
					DayCode = date,
					InstanceID = SchedInfo.Value.LegacyInstance.InstanceID.ToString(),
					Outcome = SchedInfo.Value.LegacyInstance.Outcome,
					SchdeuleStartTime = SchedInfo.Value.StartTime,
					ScheduleEndTime = SchedInfo.Value.EndTime,
					ServiceName = SchedInfo.Value.ServiceName,
					State = SchedInfo.Value.LegacyInstance.State,
					ScheduledID = SchedInfo.Key.GetHashCode().ToString()
				};
				_scheduledServices.Add(SchedInfo.Value.LegacyInstance.GetHashCode(), instancesInfo[index]);
				index++;

			}

			if (_callBacks != null)
			{
				foreach (var callBack in _callBacks)
					callBack.ScheduleCreated(instancesInfo);

			}


		}

		void _scheduler_ServiceRunRequiredEvent(object sender, EventArgs e)
		{
			TimeToRunEventArgs args = (TimeToRunEventArgs)e;

			foreach (Edge.Core.Scheduling.Objects.ServiceInstance serviceInstance in args.ServicesToRun)
			{
				serviceInstance.LegacyInstance.StateChanged += new EventHandler<Core.Services.ServiceStateChangedEventArgs>(LegacyInstance_StateChanged);
				serviceInstance.LegacyInstance.ChildServiceRequested += new EventHandler<Core.Services.ServiceRequestedEventArgs>(LegacyInstance_ChildServiceRequested);
				serviceInstance.LegacyInstance.Initialize();
			}
		}

		void LegacyInstance_ChildServiceRequested(object sender, Core.Services.ServiceRequestedEventArgs e)
		{
			try
			{
				
				legacy.ServiceInstance instance = (legacy.ServiceInstance)sender;
				e.RequestedService.ChildServiceRequested += new EventHandler<legacy.ServiceRequestedEventArgs>(LegacyInstance_ChildServiceRequested);
				e.RequestedService.StateChanged += new EventHandler<legacy.ServiceStateChangedEventArgs>(LegacyInstance_StateChanged);
			
				_scheduler.AddChildServiceToSchedule(e.RequestedService);
				
				//e.RequestedService.Initialize();
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
				instance.OutcomeReported += new EventHandler(instance_OutcomeReported);

				if (e.StateAfter == legacy.ServiceState.Ready)
					instance.Start();
				if (_scheduledServices.ContainsKey(instance.GetHashCode()))
				{

					ServiceInstanceInfo stateInfo = _scheduledServices[instance.GetHashCode()];
					stateInfo.State = e.StateAfter;
					if (e.StateAfter == legacy.ServiceState.Ready)
						stateInfo.ActualStartTime = instance.TimeStarted;

					foreach (var callBack in _callBacks)
					{
						callBack.InstanceEvent(stateInfo);

					}
				
				}
			}
			catch (Exception ex)
			{

				Edge.Core.Utilities.Log.Write("SchedulingControlForm", ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}
		}

		void instance_OutcomeReported(object sender, EventArgs e)
		{
			legacy.ServiceInstance instance = (Edge.Core.Services.ServiceInstance)sender;
			if (_scheduledServices.ContainsKey(instance.GetHashCode()))
			{
				ServiceInstanceInfo OutcomeInfo = _scheduledServices[instance.GetHashCode()];
				OutcomeInfo.Outcome = instance.Outcome;
				OutcomeInfo.ActualEndTime = instance.TimeEnded;
				foreach (var callBack in _callBacks)
					callBack.InstanceEvent(OutcomeInfo);
			}
		}
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
	}
	public interface ICallBack
	{
		[OperationContract(IsOneWay = true)]
		void ScheduleCreated(ServiceInstanceInfo[] scheduleAndStateInfo);

		[OperationContract(IsOneWay = true)]
		void InstanceEvent(ServiceInstanceInfo StateOutcomerInfo);
	}
}
