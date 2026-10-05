using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Dpi.Setup;

internal static class Remove
{
	public static void Apply(bool disableTestMode)
	{
		if (!Platform.IsAdministrator)
		{
			throw new UnauthorizedAccessException("Administrator approval is required.");
		}
		if (!string.Equals(Assembly.GetExecutingAssembly().Location, Layout.RemovalPath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Run the installed uninstaller.");
		}
		Installation installation = Installation.Load();
		string path = Path.Combine(Layout.Drivers, installation.DriverName);
		Layout.CheckPath(path);
		using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\DpiFilter"))
		{
			if (registryKey == null || !string.Equals(Convert.ToString(registryKey.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames)), "\\SystemRoot\\System32\\drivers\\" + installation.DriverName, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("The driver registration does not match this installation.");
			}
		}
		if (File.Exists(path) && Layout.HashFile(path) != installation.DriverHash)
		{
			throw new InvalidOperationException("The installed driver has been modified.");
		}
		using (RegistryKey registryKey2 = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DPI_Modifier"))
		{
			if (registryKey2 == null || Convert.ToString(registryKey2.GetValue("UninstallString")) != "\"" + Layout.RemovalPath + "\"")
			{
				throw new InvalidOperationException("The uninstall registration does not match.");
			}
		}
		Platform.StopApplication(Layout.ApplicationPath);
		if (disableTestMode && installation.TestModeChanged)
		{
			Platform.SetTestMode(installation.PreviousTestMode);
		}
		string[] filters = Layout.Filters();
		Layout.SetFilters(Layout.RemoveFilter(filters));
		try
		{
			Registry.LocalMachine.DeleteSubKeyTree("SYSTEM\\CurrentControlSet\\Services\\DpiFilter");
		}
		catch
		{
			Layout.SetFilters(filters);
			throw;
		}
		using (RegistryKey registryKey3 = Layout.OpenRun(installation.UserSid, write: true))
		{
			if (Convert.ToString(registryKey3.GetValue("DPI_Modifier")) == Layout.StartupValue)
			{
				registryKey3.DeleteValue("DPI_Modifier", throwOnMissingValue: false);
			}
		}
		Registry.LocalMachine.DeleteSubKeyTree("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DPI_Modifier", throwOnMissingSubKey: false);
		if (installation.RootAdded)
		{
			Layout.RemoveRoot(installation.Thumbprint);
		}
		if (File.Exists(Layout.Shortcut))
		{
			File.Delete(Layout.Shortcut);
		}
		if (File.Exists(path))
		{
			Platform.QueueDelete(path);
		}
		string[] array = new string[2] { "DPI.exe", "Launch.exe" };
		foreach (string path2 in array)
		{
			string path3 = Path.Combine(Layout.Root, path2);
			if (File.Exists(path3))
			{
				File.Delete(path3);
			}
		}
		File.Delete(Layout.RecordPath);
		Platform.QueueDelete(Layout.RemovalPath);
		if (Directory.GetFileSystemEntries(Layout.Root).Length == 1)
		{
			Platform.QueueDelete(Layout.Root);
		}
	}
}
