using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace Dpi.Setup;

internal static class Install
{
	public static void Apply(string userSid)
	{
		Platform.Check();
		if (!Platform.IsAdministrator)
		{
			throw new UnauthorizedAccessException("Administrator approval is required.");
		}
		if (Platform.SecureBoot)
		{
			throw new InvalidOperationException("Secure Boot is enabled. This test-signed driver cannot be installed.");
		}
		Payload payload = new Payload();
		Layout.CheckPath(Layout.Root);
		Layout.CheckPath(Layout.Drivers);
		Layout.CheckPath(Path.GetDirectoryName(Layout.Shortcut));
		using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\DpiFilter"))
		{
			if (registryKey != null)
			{
				throw new InvalidOperationException("An existing DpiFilter installation must be removed first.");
			}
		}
		using (RegistryKey registryKey2 = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DPI_Modifier"))
		{
			if (registryKey2 != null)
			{
				throw new InvalidOperationException("An existing DPI Modifier installation must be removed first.");
			}
		}
		using (RegistryKey registryKey3 = Layout.OpenRun(userSid, write: false))
		{
			if (registryKey3 != null && registryKey3.GetValue("DPI_Modifier") != null)
			{
				throw new InvalidOperationException("A DPI Modifier startup entry already exists.");
			}
		}
		if (Directory.Exists(Layout.Root) || File.Exists(Layout.Root) || File.Exists(Layout.Shortcut))
		{
			throw new InvalidOperationException("An existing DPI Modifier folder or shortcut must be removed first.");
		}
		string[] originalFilters = Layout.Filters();
		if (originalFilters.Any((string value) => string.Equals(value, "DpiFilter", StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidOperationException("An existing DpiFilter registration must be removed first.");
		}
		Installation record = new Installation
		{
			UserSid = userSid,
			DriverHash = payload.DriverHash,
			DriverName = "DpiFilter." + payload.DriverHash.Substring(0, 16) + ".sys",
			Thumbprint = payload.Certificate.Thumbprint,
			PreviousTestMode = Platform.ConfiguredTestMode()
		};
		string driver = Path.Combine(Layout.Drivers, record.DriverName);
		Layout.CheckPath(driver);
		if (File.Exists(driver))
		{
			throw new InvalidOperationException("The driver destination already exists.");
		}
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		bool flag4 = false;
		bool flag5 = false;
		bool flag6 = false;
		bool flag7 = false;
		try
		{
			Layout.CreateRoot();
			flag = true;
			string[] array = new string[3] { "DPI.exe", "Launch.exe", "Uninstall.exe" };
			foreach (string text in array)
			{
				payload.Write(text, Path.Combine(Layout.Root, text));
			}
			payload.Write("DpiFilter.sys", driver);
			flag2 = true;
			X509Certificate2 x509Certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(Layout.ApplicationPath));
			if (x509Certificate.Thumbprint != record.Thumbprint)
			{
				throw new InvalidDataException("The application signing identity does not match.");
			}
			record.RootAdded = Layout.AddRoot(payload.Certificate);
			Platform.VerifySignature(Layout.ApplicationPath);
			if (record.PreviousTestMode != true)
			{
				Platform.SetTestMode(true);
				record.TestModeChanged = true;
			}
			using (RegistryKey registryKey4 = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Services\\DpiFilter"))
			{
				flag3 = true;
				registryKey4.SetValue("Type", 1, RegistryValueKind.DWord);
				registryKey4.SetValue("Start", 3, RegistryValueKind.DWord);
				registryKey4.SetValue("ErrorControl", 1, RegistryValueKind.DWord);
				registryKey4.SetValue("ImagePath", "\\SystemRoot\\System32\\drivers\\" + record.DriverName, RegistryValueKind.ExpandString);
				registryKey4.SetValue("DisplayName", "DPI Filter");
			}
			Layout.SetFilters(Layout.AddFilter(originalFilters));
			flag4 = true;
			using (RegistryKey registryKey5 = Layout.OpenRun(userSid, write: true))
			{
				registryKey5.SetValue("DPI_Modifier", Layout.StartupValue);
				flag5 = true;
			}
			using (RegistryKey registryKey6 = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DPI_Modifier"))
			{
				flag6 = true;
				registryKey6.SetValue("DisplayName", "DPI Modifier");
				registryKey6.SetValue("DisplayVersion", "1.0");
				registryKey6.SetValue("InstallLocation", Layout.Root);
				registryKey6.SetValue("DisplayIcon", Layout.ApplicationPath + ",0");
				registryKey6.SetValue("UninstallString", "\"" + Layout.RemovalPath + "\"");
				registryKey6.SetValue("NoModify", 1, RegistryValueKind.DWord);
				registryKey6.SetValue("NoRepair", 1, RegistryValueKind.DWord);
			}
			Layout.CreateShortcut(Layout.Shortcut);
			flag7 = true;
			record.Save();
		}
		catch (Exception ex)
		{
			List<string> errors = new List<string>();
			Action<Action> action = delegate(Action action2)
			{
				try
				{
					action2();
				}
				catch (Exception ex2)
				{
					errors.Add(ex2.Message);
				}
			};
			if (flag4)
			{
				action(delegate
				{
					Layout.SetFilters(originalFilters);
				});
			}
			if (flag3)
			{
				action(delegate
				{
					Registry.LocalMachine.DeleteSubKeyTree("SYSTEM\\CurrentControlSet\\Services\\DpiFilter", throwOnMissingSubKey: false);
				});
			}
			if (flag5)
			{
				action(delegate
				{
					using RegistryKey registryKey7 = Layout.OpenRun(userSid, write: true);
					registryKey7.DeleteValue("DPI_Modifier", throwOnMissingValue: false);
				});
			}
			if (flag6)
			{
				action(delegate
				{
					Registry.LocalMachine.DeleteSubKeyTree("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DPI_Modifier", throwOnMissingSubKey: false);
				});
			}
			if (flag7 || File.Exists(Layout.Shortcut))
			{
				action(delegate
				{
					File.Delete(Layout.Shortcut);
				});
			}
			if (record.TestModeChanged)
			{
				action(delegate
				{
					Platform.SetTestMode(record.PreviousTestMode);
				});
			}
			if (record.RootAdded)
			{
				action(delegate
				{
					Layout.RemoveRoot(record.Thumbprint);
				});
			}
			if (flag2 || File.Exists(driver))
			{
				action(delegate
				{
					File.Delete(driver);
				});
			}
			if (flag)
			{
				action(delegate
				{
					string[] array2 = new string[4] { "DPI.exe", "Launch.exe", "Uninstall.exe", "installation.json" };
					foreach (string path in array2)
					{
						if (File.Exists(Path.Combine(Layout.Root, path)))
						{
							File.Delete(Path.Combine(Layout.Root, path));
						}
					}
					if (Directory.GetFileSystemEntries(Layout.Root).Length == 0)
					{
						Directory.Delete(Layout.Root);
					}
				});
			}
			throw new InvalidOperationException(ex.Message + ((errors.Count == 0) ? "" : ("\r\nRollback needs attention:\r\n" + string.Join("\r\n", errors))));
		}
	}
}
