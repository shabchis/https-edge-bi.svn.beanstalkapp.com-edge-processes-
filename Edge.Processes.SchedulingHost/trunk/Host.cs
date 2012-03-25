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
using Edge.Core.Scheduling.Objects;
using System.ServiceModel;

namespace Edge.Processes.SchedulingHost
{
	public partial class Host : ServiceBase
	{
		ScheulingCommunication _scheulingCommunication;
		public ServiceHost _serviceHost = null;
		
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
			if (_serviceHost == null)
			{
				if (_scheulingCommunication == null)
					_scheulingCommunication = new ScheulingCommunication();
				_serviceHost = new ServiceHost(_scheulingCommunication, new Uri[] { new Uri("net.pipe://localhost") });


				_serviceHost.AddServiceEndpoint(typeof(ISchedulingCommunication),
				  new NetNamedPipeBinding() { MaxBufferPoolSize = 20000000, MaxConnections = 20000000, MaxBufferSize = 20000000, MaxReceivedMessageSize = 20000000, CloseTimeout=new TimeSpan(0,3,0),OpenTimeout=new TimeSpan(0,3,0)},
				  "Scheduler");
				_serviceHost.Open();
				_scheulingCommunication.Init();
			}
		}

		protected override void OnPause()
		{
			_scheulingCommunication.Stop();
		}
		protected override void OnContinue()
		{
			_scheulingCommunication.Start();
		}

		void LegacyInstance_ChildServiceRequested(object sender, Core.Services.ServiceRequestedEventArgs e)
		{
			throw new NotImplementedException();
		}

		void LegacyInstance_StateChanged(object sender, Core.Services.ServiceStateChangedEventArgs e)
		{
			throw new NotImplementedException();
		}

		protected override void OnStop()
		{
			_scheulingCommunication.End();

		}

		
	}
	
}
