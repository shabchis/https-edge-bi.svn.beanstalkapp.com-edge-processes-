using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using Edge.Core;
using Edge.Core.Scheduling;
using Edge.Core.Services;
using Edge.Core.Utilities;

namespace Edge.Processes.DirectoryWatcher
{
	partial class DirectoryWatcher : ServiceBase
	{
		public const string FilePath = "FilePath";
		public const string LogSource = "DirectoryWatcher";
		Dictionary<FileSystemWatcher, DirectoryWatchInfo> _watchers = new Dictionary<FileSystemWatcher, DirectoryWatchInfo>();

		public DirectoryWatcher()
		{
			InitializeComponent();
		}
		//for debug only
		//public void start()
		//{
		//    OnStart(null);
		//}
		protected override void OnStart(string[] args)
		{
			var configuration = (DirectoryWatcherConfiguration)ConfigurationManager.GetSection(DirectoryWatcherConfiguration.SectionName);

			foreach (DirectoryElement dir in configuration.Directories)
			{
				if (String.IsNullOrWhiteSpace(dir.Path) || !Directory.Exists(dir.Path))
					throw new ConfigurationErrorsException("Invalid path specified.",
						dir.CurrentConfiguration!= null ?
							dir.CurrentConfiguration.FilePath : string.Empty,
						dir.ElementInformation.LineNumber);

				if (String.IsNullOrWhiteSpace(dir.ServiceName))
					throw new ConfigurationErrorsException("Invalid service name specified.",
							dir.CurrentConfiguration != null ?
							dir.CurrentConfiguration.FilePath : string.Empty,
						dir.ElementInformation.LineNumber);

				FileSystemWatcher watcher = new FileSystemWatcher(dir.Path);
				watcher.NotifyFilter = NotifyFilters.FileName;
				watcher.IncludeSubdirectories = dir.IncludeSubdirs;
				if (!String.IsNullOrEmpty(dir.Filter))
					watcher.Filter = dir.Filter;

				watcher.Changed += new FileSystemEventHandler(watcher_Changed);
				watcher.Created += new FileSystemEventHandler(watcher_Changed);

				_watchers.Add(watcher, new DirectoryWatchInfo() { Config = dir, Watcher = watcher });
			}

			OnContinue();
		}

		protected override void OnContinue()
		{
			foreach (FileSystemWatcher watcher in _watchers.Keys)
				watcher.EnableRaisingEvents = true;
		}

		protected override void OnPause()
		{
			foreach (FileSystemWatcher watcher in _watchers.Keys)
				watcher.EnableRaisingEvents = false;
		}

		protected override void OnStop()
		{
			foreach (FileSystemWatcher watcher in _watchers.Keys)
				watcher.Dispose();
		}

		void watcher_Changed(object sender, FileSystemEventArgs e)
		{
			Log.Write(LogSource, String.Format("{0} has been {1} ", e.Name, e.ChangeType), LogMessageType.Information);

			FileSystemWatcher watcher = (FileSystemWatcher)sender;
			DirectoryWatchInfo info;
			if (!_watchers.TryGetValue(watcher, out info))
			{
				Log.Write(String.Format("No watcher found for {0} - check your configuration.", e.Name), LogMessageType.Warning);
				return;
			}

			ServiceClient<IScheduleManager> scheduler;
			
			try
			{
				scheduler =
					!String.IsNullOrWhiteSpace(info.Config.SchedulerUrl) || !String.IsNullOrWhiteSpace(info.Config.SchedulerConfiguration) ?
						new ServiceClient<IScheduleManager>(info.Config.SchedulerConfiguration, info.Config.SchedulerUrl) :
						new ServiceClient<IScheduleManager>();
			}
			catch (Exception ex)
			{
				Log.Write(String.Format(
					"Could not create a connection to the scheduler{0}.",
					String.IsNullOrWhiteSpace(info.Config.SchedulerUrl) ? string.Empty : " at " + info.Config.SchedulerUrl
				), ex);
				return;
			}

			// Make the request to the schedule manager
			using (scheduler)
			{
				var options = new SettingsCollection();

				try
				{
					foreach (var option in info.Config.ServiceOptions)
						options.Add(option.Key, option.Value.Replace("{" + DirectoryWatcher.FilePath + "}", e.FullPath));
				}
				catch (Exception ex)
				{
					Log.Write("Error creating options for the service from the <Directory> configuration.", ex);
				}

				try
				{
					scheduler.Service.AddToSchedule(info.Config.ServiceName, info.Config.AccountID, DateTime.Now, options);
				}
				catch (Exception ex)
				{
					Log.Write(String.Format(
						"Error trying to call AddToSchedule on the scheduler{0}.",
						String.IsNullOrWhiteSpace(info.Config.SchedulerUrl) ? string.Empty : " at " + info.Config.SchedulerUrl
					), ex);

				}
			}
		}

	}

	public class DirectoryWatchInfo
	{
		public DirectoryElement Config;
		public FileSystemWatcher Watcher;
	}
}
