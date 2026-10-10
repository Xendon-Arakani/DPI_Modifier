using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Dpi.Setup;

internal static class Launch
{
	[STAThread]
	private static void Main(string[] args)
	{
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(directoryName, "DPI.exe"), (Array.IndexOf(args, "--hidden") >= 0) ? "--hidden" : "");
		processStartInfo.UseShellExecute = true;
		processStartInfo.WorkingDirectory = directoryName;
		Process.Start(processStartInfo);
	}
}
