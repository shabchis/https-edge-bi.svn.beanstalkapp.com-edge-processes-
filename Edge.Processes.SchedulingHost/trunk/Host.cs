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

namespace Edge.Processes.SchedulingHost
{
	public partial class Host : ServiceBase
	{
		ServiceExecutionHost _executionHost;
		SchedulingHost _schedulingHost;
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
			if (_wcfHost == null)
			{
				if (_executionHost == null)
				{
					_executionHost = new ServiceExecutionHost();
					_schedulingHost = new SchedulingHost();
				}

				_wcfHost = new ServiceHost(_schedulingHost);
				_wcfHost.Open();

				_schedulingHost.Init(_executionHost.Environment);
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
