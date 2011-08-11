using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;

namespace Edge.Processes.DirectoryWatcher
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main()
		{
			//for debug only
			//DirectoryWatcher d = new DirectoryWatcher();
			//d.start();
			//System.Threading.Thread.CurrentThread.Suspend();
			ServiceBase[] ServicesToRun;
			ServicesToRun = new ServiceBase[] 
			{ 
			    new DirectoryWatcher() 
			};
			ServiceBase.Run(ServicesToRun);
		}
	}
}
