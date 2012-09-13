using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using Edge.Core.Scheduling;
using Edge.Core.Configuration;
using Edge.Core.Services;
using System.ServiceModel;
using System.ServiceModel.Description;
using Edge.Core.Services.Scheduling;

namespace Edge.Processes.SchedulingHost
{
	public partial class Host : ServiceBase
	{
		ServiceExecutionHost _executionHost;
		//Scheduler _schedulingHost;
		SchedulingHost _schedulingHost;
		public ServiceHost _wcfHost = null;
		ServiceEnvironment _environment;
		
		public Host()
		{
			InitializeComponent();			
		}
		
		public void Debug()
		{
			OnStart(null);
		}
		
		protected override void OnStart(string[] args)
		{
			//TODO: FROM CONFIGURATION
			#region temp
			var envConfig = new ServiceEnvironmentConfiguration()
			{
				DefaultHostName = "Johnny",
				ConnectionString = "Data Source=bi_rnd;Initial Catalog=EdgeSystem;Integrated Security=true",
				SP_HostList = "Service_HostList",
				SP_HostRegister = "Service_HostRegister",
				SP_HostUnregister = "Service_HostUnregister",
				SP_InstanceSave = "Service_InstanceSave",
				SP_InstanceReset = "Service_InstanceReset",
				SP_EnvironmentEventList = "Service_EnvironmentEventList",
				SP_EnvironmentEventRegister = "Service_EnvironmentEventRegister"

			};

			#endregion
			if (_wcfHost == null)
			{
				if (_executionHost == null)
				{
					//temp
					_environment=new ServiceEnvironment(envConfig);
					_executionHost = new ServiceExecutionHost(_environment.EnvironmentConfiguration.DefaultHostName,_environment);
					
					
				}

				_schedulingHost = new SchedulingHost(_executionHost);				

				_wcfHost = new ServiceHost(_schedulingHost);
				_wcfHost.Open();

				_schedulingHost.Init();
				_schedulingHost.Start();
			
			}
		}
		
		protected override void OnPause()
		{
			_schedulingHost.Stop();
		}
		
		protected override void OnContinue()
		{
			_schedulingHost.Start();
		}
		
		protected override void OnStop()
		{
			_schedulingHost.Stop();
		}		
	}
	
}
