using System.Collections.Generic;

namespace VersionOne.ServiceHost.SubversionServices
{
	public class PropertiesCollection : Dictionary<string, Dictionary<string, string>>
	{
		
	}

	public class RevisionPropertyCollection : Dictionary<string, string>
	{
		public readonly int Revision;

		public RevisionPropertyCollection(int revision)
		{
			Revision = revision;
		}
	}
}
