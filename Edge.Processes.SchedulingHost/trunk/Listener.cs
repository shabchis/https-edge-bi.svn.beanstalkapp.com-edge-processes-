using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using Edge.Core.Configuration;
using Edge.Core.Scheduling;
using Edge.Core.Scheduling.Objects;
using Edge.Core.Services;

namespace Edge.Processes.SchedulingHost
{
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Single)]
	public class Listener : IScheduleManager, IDisposable
	{
		#region members
		ServiceHost _wcfHost;
		Scheduler _scheduler;
		#endregion	
		#region ctor
		public Listener(Scheduler scheduler)
		{
			_scheduler = scheduler;
		}
		#endregion
		#region public methods
		public void Start()
		{
			_wcfHost = new ServiceHost(this);
			_wcfHost.Open();
		}
		public bool AddToSchedule(string serviceName, int accountID, DateTime targetTime, Edge.Core.SettingsCollection options)
		{
			bool respond = true;
			try
			{
				ServiceConfiguration myServiceConfiguration = new ServiceConfiguration();
				ServiceConfiguration baseConfiguration = new ServiceConfiguration();
				ActiveServiceElement activeServiceElement = new ActiveServiceElement(EdgeServicesConfiguration.Current.Accounts.GetAccount(accountID).Services[serviceName]);
				//activeServiceElement.Workflow[0].n
				if (options != null)
				{
					foreach (string option in options.Keys)
						activeServiceElement.Options[option] = options[option];
				}
				ServiceElement serviceElement = EdgeServicesConfiguration.Current.Services[serviceName];
				//base configuration;
				baseConfiguration.Name = serviceElement.Name;
				baseConfiguration.MaxConcurrent = serviceElement.MaxInstances;
				baseConfiguration.MaxCuncurrentPerProfile = serviceElement.MaxInstancesPerAccount;
				//configuration per profile
				myServiceConfiguration = new ServiceConfiguration();				
				myServiceConfiguration.Name = activeServiceElement.Name;
				if (activeServiceElement.Options.ContainsKey("ServicePriority"))
					myServiceConfiguration.priority = int.Parse(activeServiceElement.Options["ServicePriority"]);
				myServiceConfiguration.MaxConcurrent = (activeServiceElement.MaxInstances == 0) ? 9999 : activeServiceElement.MaxInstances;
				myServiceConfiguration.MaxCuncurrentPerProfile = (activeServiceElement.MaxInstancesPerAccount == 0) ? 9999 : activeServiceElement.MaxInstancesPerAccount;
				myServiceConfiguration.LegacyConfiguration = activeServiceElement;
				//        //scheduling rules 
				myServiceConfiguration.SchedulingRules.Add(new SchedulingRule()
				{
					Scope = SchedulingScope.UnPlanned,
					SpecificDateTime = DateTime.Now,
					MaxDeviationAfter = new TimeSpan(0, 0, 45, 0, 0),
					Hours = new List<TimeSpan>(),
					GuidForUnplaned = Guid.NewGuid()
				});
				myServiceConfiguration.SchedulingRules[0].Hours.Add(new TimeSpan(0, 0, 0, 0));
				myServiceConfiguration.BaseConfiguration = baseConfiguration;
				Profile profile = new Profile()
				{
					ID = accountID,
					Name = accountID.ToString(),
					Settings = new Dictionary<string, object>()
				};
				profile.Settings.Add("AccountID", accountID);
				myServiceConfiguration.SchedulingProfile = profile;
				_scheduler.AddNewServiceToSchedule(myServiceConfiguration);
			}
			catch (Exception ex)
			{
				respond = false;
				Edge.Core.Utilities.Log.Write("AddManualServiceListner", ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}



			return respond;
		}
		public bool FormAddToSchedule(string serviceName, int accountID, DateTime targetTime, Edge.Core.SettingsCollection options, ServicePriority servicePriority)
		{
			bool respond = true;
			try
			{
				ServiceConfiguration myServiceConfiguration = new ServiceConfiguration();
				ServiceConfiguration baseConfiguration = new ServiceConfiguration();
				ActiveServiceElement activeServiceElement = new ActiveServiceElement(EdgeServicesConfiguration.Current.Accounts.GetAccount(accountID).Services[serviceName]);
				if (options != null)
				{
					foreach (string option in options.Keys)
						activeServiceElement.Options[option] = options[option];
				}
				ServiceElement serviceElement = EdgeServicesConfiguration.Current.Services[serviceName];

				//base configuration;
				baseConfiguration.Name = serviceElement.Name;
				baseConfiguration.MaxConcurrent = serviceElement.MaxInstances;
				baseConfiguration.MaxCuncurrentPerProfile = serviceElement.MaxInstancesPerAccount;
				//configuration per profile
				myServiceConfiguration = new ServiceConfiguration();
				myServiceConfiguration.Name = activeServiceElement.Name;
				myServiceConfiguration.priority = (int)servicePriority;
				if (activeServiceElement.Options.ContainsKey("ServicePriority"))
					myServiceConfiguration.priority = int.Parse(activeServiceElement.Options["ServicePriority"]);
				myServiceConfiguration.MaxConcurrent = (activeServiceElement.MaxInstances == 0) ? 9999 : activeServiceElement.MaxInstances;
				myServiceConfiguration.MaxCuncurrentPerProfile = (activeServiceElement.MaxInstancesPerAccount == 0) ? 9999 : activeServiceElement.MaxInstancesPerAccount;
				myServiceConfiguration.LegacyConfiguration = activeServiceElement;
				//        //scheduling rules 
				myServiceConfiguration.SchedulingRules.Add(new SchedulingRule()
				{
					Scope = SchedulingScope.UnPlanned,
					SpecificDateTime = targetTime,
					MaxDeviationAfter = new TimeSpan(0, 0, 45, 0, 0),
					Hours = new List<TimeSpan>(),
					GuidForUnplaned = Guid.NewGuid()
				});
				myServiceConfiguration.SchedulingRules[0].Hours.Add(new TimeSpan(0, 0, 0, 0));
				myServiceConfiguration.BaseConfiguration = baseConfiguration;
				Profile profile = new Profile()
				{
					ID = accountID,
					Name = accountID.ToString(),
					Settings = new Dictionary<string, object>()
				};
				profile.Settings.Add("AccountID", accountID);
				myServiceConfiguration.SchedulingProfile = profile;
				_scheduler.AddNewServiceToSchedule(myServiceConfiguration);
			}
			catch (Exception ex)
			{
				respond = false;
				Edge.Core.Utilities.Log.Write("AddManualServiceListner", ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}
			return respond;
		}
		public bool FormAddToSchedule(ActiveServiceElement activeServiceElement, AccountElement account, DateTime targetTime, Edge.Core.SettingsCollection options, ServicePriority servicePriority)
		{
			bool respond = true;
			try
			{
			ServiceConfiguration myServiceConfiguration = new ServiceConfiguration();
			ServiceConfiguration baseConfiguration = new ServiceConfiguration();
			if (options != null)
			{
				foreach (string option in options.Keys)
					activeServiceElement.Options[option] = options[option];
			}				
			//ServiceElement serviceElement = EdgeServicesConfiguration.Current.Services[activeServiceElement.Name];
			//base configuration;
			baseConfiguration.Name = activeServiceElement.Name;
			baseConfiguration.MaxConcurrent = activeServiceElement.MaxInstances;
			baseConfiguration.MaxCuncurrentPerProfile = activeServiceElement.MaxInstancesPerAccount;
			//configuration per profile
			myServiceConfiguration = new ServiceConfiguration();			
			myServiceConfiguration.Name = activeServiceElement.Name;
			myServiceConfiguration.priority = (int)servicePriority;
			if (activeServiceElement.Options.ContainsKey("ServicePriority"))
				myServiceConfiguration.priority = int.Parse(activeServiceElement.Options["ServicePriority"]);
			myServiceConfiguration.MaxConcurrent = (activeServiceElement.MaxInstances == 0) ? 9999 : activeServiceElement.MaxInstances;
			myServiceConfiguration.MaxCuncurrentPerProfile = (activeServiceElement.MaxInstancesPerAccount == 0) ? 9999 : activeServiceElement.MaxInstancesPerAccount;
			myServiceConfiguration.LegacyConfiguration = activeServiceElement;
			//        //scheduling rules 
			myServiceConfiguration.SchedulingRules.Add(new SchedulingRule()
			{
				Scope = SchedulingScope.UnPlanned,
				SpecificDateTime = targetTime,
				MaxDeviationAfter = new TimeSpan(0, 0, 120, 0, 0),
				Hours = new List<TimeSpan>(),
				GuidForUnplaned = Guid.NewGuid()
			});
			myServiceConfiguration.SchedulingRules[0].Hours.Add(new TimeSpan(0, 0, 0, 0));
			myServiceConfiguration.BaseConfiguration = baseConfiguration;
			Profile profile = new Profile()
			{
				ID = account.ID,
				Name =account.Name,
				Settings = new Dictionary<string, object>()
			};
			profile.Settings.Add("AccountID", account.ID.ToString());
			myServiceConfiguration.SchedulingProfile = profile;
		    
			_scheduler.AddNewServiceToSchedule(myServiceConfiguration);
			
			}
			catch (Exception ex)
			{
				respond = false;
				Edge.Core.Utilities.Log.Write("AddManualServiceListner", ex.Message, ex, Edge.Core.Utilities.LogMessageType.Error);
			}
			return respond;
		}
		public void Dispose()
		{
			if (_wcfHost != null)
				((IDisposable)_wcfHost).Dispose();
		}
		#endregion
		#region IScheduleManager Members
		public void BuildSchedule()
		{			
		}
		#endregion
	}
}
