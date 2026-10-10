using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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


	public static void QueueDelete(string path)
	{
		if (!MoveFileEx(path, IntPtr.Zero, 4u))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
	}


    public static void StopApplication(string path, bool legacy = false)
    {
        foreach (var process in Process.GetProcessesByName("DPI"))
        using (process)
        {
            var handle = OpenProcess(4096u, false, process.Id);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Close DPI Modifier before continuing.");
            bool matches;
            try
            {
                var name = new StringBuilder(32768);
                uint size = (uint)name.Capacity;
                matches = QueryFullProcessImageName(handle, 0u, name, ref size) &&
                    string.Equals(name.ToString(), path, StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(handle); }
            if (!matches) continue;
            if (legacy) process.Kill();
            else
                EnumWindows((window, unused) => {
                    GetWindowThreadProcessId(window, out var id);
                    if (id == process.Id) PostMessage(window, 0x8040, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            if (!process.WaitForExit(15000))
                throw new InvalidOperationException("DPI Modifier could not restore mouse settings and close. Exit it from its tray menu, then retry.");
        }
    }
    public static void VerifyDriver(string path)
    {
        if (!string.Equals(Layout.HashFile(path), Installation.SignedDriverHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The signed mouse driver does not match the approved package.");
        VerifySignature(path);
        using (var signer = new System.Security.Cryptography.X509Certificates.X509Certificate2(
            System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path)))
            if (!signer.Subject.Contains("CN=Microsoft Windows Hardware Compatibility Publisher"))
                throw new InvalidDataException("The mouse driver does not have the expected Microsoft signature.");
    }
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

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
				throw new InvalidOperationException("The packaged file signature was not trusted: 0x" + num.ToString("X8"));
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
