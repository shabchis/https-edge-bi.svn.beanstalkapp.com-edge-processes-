using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;

namespace Edge.Processes.DirectoryWatcher
{
	public class DirectoryWatcherConfiguration : ConfigurationSection
	{
		public const string SectionName = "edge.processes.directoryWatcher";

		[ConfigurationProperty("", IsDefaultCollection = true, IsRequired = true)]
		public DirectoryElementCollection Directories
		{
			get { return (DirectoryElementCollection) base[""]; }
		}
	}

	public class DirectoryElementCollection : Edge.Core.Configuration.ConfigurationElementCollectionBase<DirectoryElement>
	{
		public override ConfigurationElementCollectionType CollectionType
		{
			get { return ConfigurationElementCollectionType.BasicMap; }
		}

		protected override ConfigurationElement CreateNewElement()
		{
			return new DirectoryElement();
		}

		protected override object GetElementKey(ConfigurationElement element)
		{
			return element.ElementInformation.LineNumber;
		}

		protected override string ElementName
		{
			get { return "Directory"; }
		}
	}


	public class DirectoryElement : ConfigurationElement
	{
		[ConfigurationProperty("Path", IsRequired = true)]
		public string Path
		{
			get { return (string)base["Path"]; }
		}

		[ConfigurationProperty("Filter")]
		public string Filter
		{
			get { return (string)base["Filter"]; }
		}

		[ConfigurationProperty("ServiceName")]
		public string ServiceName
		{
			get { return (string)base["ServiceName"]; }
		}

		[ConfigurationProperty("AccountID", DefaultValue=-1)]
		public int AccountID
		{
			get { return (int)base["AccountID"]; }
		}

		[ConfigurationProperty("SchedulerUrl")]
		public string SchedulerUrl
		{
			get { return (string)base["SchedulerUrl"]; }
		}

		[ConfigurationProperty("SchedulerConfiguration")]
		public string SchedulerConfiguration
		{
			get { return (string)base["SchedulerConfiguration"]; }
		}

		[ConfigurationProperty("IncludeSubdirs")]
		public bool IncludeSubdirs
		{
			get { return (bool)base["IncludeSubdirs"]; }
		}

		// Support for options
		public readonly Dictionary<string, string> ServiceOptions = new Dictionary<string, string>();
		protected override bool OnDeserializeUnrecognizedAttribute(string name, string value)
		{
			this.ServiceOptions[name] = value;
			return true;
		}

	}
}
