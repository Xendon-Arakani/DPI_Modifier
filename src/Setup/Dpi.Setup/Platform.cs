using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Dpi.Setup;

internal static class Platform
{
	private struct Integrity
	{
		public uint Length;

		public uint Options;
	}

	private struct SystemInfo
	{
		public ushort Architecture;

		public ushort Reserved;

		public uint PageSize;

		public IntPtr Minimum;

		public IntPtr Maximum;

		public IntPtr Mask;

		public uint Count;

		public uint Type;

		public uint Granularity;

		public ushort Level;

		public ushort Revision;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct TrustFile
	{
		public uint Size;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string Path;

		public IntPtr Handle;

		public IntPtr Subject;
	}

	private struct TrustData
	{
		public uint Size;

		public IntPtr Policy;

		public IntPtr Client;

		public uint Ui;

		public uint Revocation;

		public uint Choice;

		public IntPtr File;

		public uint Action;

		public IntPtr State;

		public IntPtr Url;

		public uint Flags;

		public uint Context;

		public IntPtr Settings;
	}

	public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

	public static bool TestMode
	{
		get
		{
			Integrity information = new Integrity
			{
				Length = 8u
			};
			if (NtQuerySystemInformation(103, ref information, 8u, out var _) != 0)
			{
				throw new InvalidOperationException("Windows code integrity status could not be read.");
			}
			return (information.Options & 2) != 0;
		}
	}

	public static bool SecureBoot
	{
		get
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State");
			return registryKey != null && Convert.ToInt32(registryKey.GetValue("UEFISecureBootEnabled", 0)) != 0;
		}
	}

	public static void Check()
	{
		GetNativeSystemInfo(out var information);
		if (information.Architecture != 9 || !Environment.Is64BitProcess || Environment.OSVersion.Version.Major < 10)
		{
			throw new InvalidOperationException("Windows 10 or 11 on an x64 processor is required.");
		}
	}

	public static bool? ConfiguredTestMode()
	{
		ManagementScope managementScope = new ManagementScope("root\\WMI", new ConnectionOptions
		{
			Impersonation = ImpersonationLevel.Impersonate,
			EnablePrivileges = true
		});
		managementScope.Connect();
		using ManagementObject managementObject = new ManagementObject(managementScope, new ManagementPath("BcdObject.Id=\"{fa926493-6f1c-4193-a414-58f0b2456d1e}\",StoreFilePath=\"\""), null);
		using ManagementBaseObject managementBaseObject = managementObject.InvokeMethod("EnumerateElements", null, null);
		if (managementBaseObject == null || !Convert.ToBoolean(managementBaseObject["ReturnValue"]))
		{
			throw new InvalidOperationException("The Windows boot setting could not be read.");
		}
		if (!(managementBaseObject["Elements"] is ManagementBaseObject[] array))
		{
			throw new InvalidOperationException("The Windows boot setting has an unexpected format.");
		}
		bool? result = null;
		ManagementBaseObject[] array2 = array;
		foreach (ManagementBaseObject managementBaseObject2 in array2)
		{
			using (managementBaseObject2)
			{
				if (Convert.ToUInt32(managementBaseObject2["Type"]) == 369098825)
				{
					result = Convert.ToBoolean(managementBaseObject2["Boolean"]);
				}
			}
		}
		return result;
	}

	public static void SetTestMode(bool? enabled)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "bcdedit.exe"), enabled.HasValue ? ("/set {current} testsigning " + (enabled.Value ? "on" : "off")) : "/deletevalue {current} testsigning");
		processStartInfo.UseShellExecute = false;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		using Process process = Process.Start(processStartInfo);
		string text = process.StandardOutput.ReadToEnd();
		string text2 = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException("Windows test mode could not be changed.\r\n" + text + text2);
		}
	}

	public static void QueueDelete(string path)
	{
		if (!MoveFileEx(path, IntPtr.Zero, 4u))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
	}

	public static void StopApplication(string path)
	{
		Process[] processesByName = Process.GetProcessesByName("DPI");
		foreach (Process process in processesByName)
		{
			using (process)
			{
				IntPtr intPtr = OpenProcess(4096u, inherit: false, process.Id);
				if (intPtr == IntPtr.Zero)
				{
					continue;
				}
				bool flag;
				try
				{
					StringBuilder stringBuilder = new StringBuilder(32768);
					uint size = (uint)stringBuilder.Capacity;
					flag = QueryFullProcessImageName(intPtr, 0u, stringBuilder, ref size) && string.Equals(stringBuilder.ToString(), path, StringComparison.OrdinalIgnoreCase);
				}
				finally
				{
					CloseHandle(intPtr);
				}
				if (flag)
				{
					process.Kill();
					if (!process.WaitForExit(10000))
					{
						throw new InvalidOperationException("DPI could not be closed.");
					}
				}
			}
		}
	}

	public static void VerifySignature(string path)
	{
		TrustFile structure = new TrustFile
		{
			Size = (uint)Marshal.SizeOf(typeof(TrustFile)),
			Path = path
		};
		IntPtr intPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
		Marshal.StructureToPtr(structure, intPtr, fDeleteOld: false);
		TrustData data = new TrustData
		{
			Size = (uint)Marshal.SizeOf(typeof(TrustData)),
			Ui = 2u,
			Choice = 1u,
			File = intPtr,
			Action = 1u,
			Flags = 4096u
		};
		Guid action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
		try
		{
			int num = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
			if (num != 0)
			{
				throw new InvalidOperationException("The application signature was not trusted: 0x" + num.ToString("X8"));
			}
		}
		finally
		{
			data.Action = 2u;
			WinVerifyTrust(new IntPtr(-1), ref action, ref data);
			Marshal.DestroyStructure(intPtr, typeof(TrustFile));
			Marshal.FreeHGlobal(intPtr);
		}
	}

	[DllImport("ntdll.dll")]
	private static extern int NtQuerySystemInformation(int kind, ref Integrity information, uint length, out uint returned);

	[DllImport("kernel32.dll")]
	private static extern void GetNativeSystemInfo(out SystemInfo information);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool MoveFileEx(string source, IntPtr destination, uint flags);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr handle);

	[DllImport("user32.dll")]
	public static extern bool SetProcessDPIAware();

	[DllImport("wintrust.dll", ExactSpelling = true)]
	private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}
