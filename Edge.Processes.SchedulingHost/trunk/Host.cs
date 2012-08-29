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
		Scheduler _schedulingHost;
		public ServiceHost _wcfHost = null;		
		
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
				ConnectionString = "Data Source=bi_rnd;Initial Catalog=EdgeSystem;Integrated Security=true",
				HostListSP = "Service_HostList",
				HostRegisterSP = "Service_HostRegister",
				HostUnregisterSP = "Service_HostUnregister"
			};

			#endregion
			if (_wcfHost == null)
			{
				if (_executionHost == null)
				{
					//temp
					_executionHost = new ServiceExecutionHost("Johnny", envConfig);
					
				}

				_schedulingHost = new Scheduler(_executionHost.Environment);
				_schedulingHost.Start();
				//_wcfHost.Open();

				//_schedulingHost.Init(_executionHost.Environment);
				//_schedulingHost.Start();
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
