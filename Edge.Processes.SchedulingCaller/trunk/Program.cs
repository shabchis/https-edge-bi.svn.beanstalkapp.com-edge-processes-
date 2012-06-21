using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Edge.Core.Services;
using Edge.Core.Scheduling;
using Edge.Core;

namespace Edge.Processes.SchedulingCaller
{
	class Program
	{
		static void Main(string[] args)
		{
			foreach (string arg in args)
			{
				string serviceName = arg;

				ServiceClient<IScheduleManager> scheduleManager = new ServiceClient<IScheduleManager>();
				//run the service
				scheduleManager.Service.AddToSchedule(serviceName, -1, DateTime.Now, new SettingsCollection());

			}
		}
	}
}
