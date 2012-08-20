using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using Edge.Core.Configuration;
using System.Threading;
using Edge.Core.Services.Configuration;

namespace Edge.Processes.SchedulingHost
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main(string[] args)
		{

			string configFileName = EdgeServicesConfiguration.DefaultFileName;
			if (args.Length > 0 && args[0].StartsWith("/") && args[0].Length > 1)
			{
				configFileName = args[0].Substring(1);
			}
			EdgeServicesConfiguration.Load(configFileName);
			ServiceBase[] ServicesToRun;
			Host host=new Host();
			ServicesToRun = new ServiceBase[] 
			{ 
				host
			};
#if DEBUG
			host.Debug();
	while (true)
	{
		
	}
			
#else
			ServiceBase.Run(ServicesToRun);
#endif



		}
	}
}
