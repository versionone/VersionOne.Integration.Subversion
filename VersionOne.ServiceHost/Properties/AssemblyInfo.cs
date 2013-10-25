using System.Reflection;

[assembly: AssemblyTitle("VersionOne.ServiceHost")]

#if !DEBUG
[assembly: AssemblyDelaySign(false)]
[assembly: AssemblyKeyFile("..\\..\\..\\..\\Common\\SigningKey\\VersionOne.snk")]
[assembly: AssemblyKeyName("")]
#endif
[assembly: AssemblyCompanyAttribute("VersionOne")]
[assembly: AssemblyProductAttribute("VersionOne.ServiceHost")]
[assembly: AssemblyCopyrightAttribute("Copyright © 2013")]
