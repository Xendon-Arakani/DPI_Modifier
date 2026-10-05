using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dpi.Setup;

internal static class Layout
{
	[ComImport]
	[Guid("00021401-0000-0000-C000-000000000046")]
	private class ShellLink
	{
	}

	[ComImport]
	[Guid("000214F9-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface ShellLinkW
	{
		void GetPath(IntPtr path, int length, IntPtr data, uint flags);

		void GetIdList(out IntPtr list);

		void SetIdList(IntPtr list);

		void GetDescription(IntPtr description, int length);

		void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);

		void GetWorkingDirectory(IntPtr directory, int length);

		void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

		void GetArguments(IntPtr arguments, int length);

		void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

		void GetHotkey(out short hotkey);

		void SetHotkey(short hotkey);

		void GetShowCommand(out int command);

		void SetShowCommand(int command);

		void GetIconLocation(IntPtr path, int length, out int index);

		void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);

		void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

		void Resolve(IntPtr window, uint flags);

		void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
	}

	public const string Service = "SYSTEM\\CurrentControlSet\\Services\\DpiFilter";

	public const string MouseClass = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E96F-E325-11CE-BFC1-08002BE10318}";

	public const string Removal = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DPI_Modifier";

	public const string Run = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DPI_Modifier");

	public static readonly string Drivers = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");

	public static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DPI_Modifier.lnk");

	public static string ApplicationPath => Path.Combine(Root, "DPI.exe");

	public static string LaunchPath => Path.Combine(Root, "Launch.exe");

	public static string RemovalPath => Path.Combine(Root, "Uninstall.exe");

	public static string RecordPath => Path.Combine(Root, "installation.json");

	public static string StartupValue => "\"" + LaunchPath + "\" --hidden";

	public static string Hash(byte[] bytes)
	{
		using SHA256 sHA = SHA256.Create();
		return BitConverter.ToString(sHA.ComputeHash(bytes)).Replace("-", "");
	}

	public static string HashFile(string path)
	{
		using FileStream inputStream = File.OpenRead(path);
		using SHA256 sHA = SHA256.Create();
		return BitConverter.ToString(sHA.ComputeHash(inputStream)).Replace("-", "");
	}

	public static void CheckPath(string path)
	{
		for (string text = Path.GetFullPath(path); text != null; text = Path.GetDirectoryName(text))
		{
			if ((File.Exists(text) || Directory.Exists(text)) && (File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0)
			{
				throw new InvalidOperationException("A redirected installation path is not allowed.");
			}
		}
	}

	public static void CreateRoot()
	{
		DirectorySecurity directorySecurity = new DirectorySecurity();
		SecurityIdentifier owner = new SecurityIdentifier("S-1-5-32-544");
		directorySecurity.SetOwner(owner);
		directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		string[] array = new string[3] { "S-1-5-32-544", "S-1-5-18", "S-1-5-32-545" };
		foreach (string text in array)
		{
			directorySecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(text), (text == "S-1-5-32-545") ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
		}
		Directory.CreateDirectory(Root, directorySecurity);
	}

	public static string[] Filters()
	{
		using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E96F-E325-11CE-BFC1-08002BE10318}");
		if (registryKey == null)
		{
			throw new InvalidOperationException("The Windows mouse class is missing.");
		}
		object value = registryKey.GetValue("UpperFilters");
		if (value == null)
		{
			return new string[0];
		}
		if (!(value is string[]))
		{
			throw new InvalidOperationException("The mouse filter setting has an unexpected format.");
		}
		return (string[])value;
	}

	public static string[] AddFilter(string[] filters)
	{
		List<string> list = new List<string>(filters.Where((string value) => !string.Equals(value, "DpiFilter", StringComparison.OrdinalIgnoreCase)));
		int num = list.FindIndex((string value) => string.Equals(value, "mouclass", StringComparison.OrdinalIgnoreCase));
		if (num < 0)
		{
			list.Add("DpiFilter");
		}
		else
		{
			list.Insert(num, "DpiFilter");
		}
		return list.ToArray();
	}

	public static string[] RemoveFilter(string[] filters)
	{
		return filters.Where((string value) => !string.Equals(value, "DpiFilter", StringComparison.OrdinalIgnoreCase)).ToArray();
	}

	public static void SetFilters(string[] filters)
	{
		using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E96F-E325-11CE-BFC1-08002BE10318}", writable: true);
		if (filters.Length == 0)
		{
			registryKey.DeleteValue("UpperFilters", throwOnMissingValue: false);
		}
		else
		{
			registryKey.SetValue("UpperFilters", filters, RegistryValueKind.MultiString);
		}
	}

	public static RegistryKey OpenRun(string userSid, bool write)
	{
		if (userSid == null || new SecurityIdentifier(userSid).Value != userSid)
		{
			throw new InvalidOperationException("The Windows user identifier is invalid.");
		}
		using RegistryKey registryKey = Registry.Users.OpenSubKey(userSid, write);
		if (registryKey == null)
		{
			throw new InvalidOperationException("The original Windows account is not signed in.");
		}
		return write ? registryKey.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run") : registryKey.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
	}

	public static bool AddRoot(X509Certificate2 certificate)
	{
		using X509Store x509Store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
		x509Store.Open(OpenFlags.ReadWrite);
		if (x509Store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false).Count != 0)
		{
			return false;
		}
		x509Store.Add(certificate);
		return true;
	}

	public static void RemoveRoot(string thumbprint)
	{
		if (!Regex.IsMatch(thumbprint ?? "", "^[A-Fa-f0-9]{40}$"))
		{
			throw new InvalidOperationException("The certificate reference is invalid.");
		}
		using X509Store x509Store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
		x509Store.Open(OpenFlags.ReadWrite);
		X509Certificate2Enumerator enumerator = x509Store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).GetEnumerator();
		while (enumerator.MoveNext())
		{
			X509Certificate2 current = enumerator.Current;
			if (current.Subject != "CN=DPI Modifier Local")
			{
				throw new InvalidOperationException("The certificate identity does not match.");
			}
			x509Store.Remove(current);
		}
	}

	public static void CreateShortcut(string path)
	{
		object obj = new ShellLink();
		try
		{
			ShellLinkW shellLinkW = (ShellLinkW)obj;
			shellLinkW.SetPath(LaunchPath);
			shellLinkW.SetWorkingDirectory(Root);
			shellLinkW.SetIconLocation(ApplicationPath, 0);
			((IPersistFile)obj).Save(path, fRemember: true);
		}
		finally
		{
			Marshal.FinalReleaseComObject(obj);
		}
	}
}
