using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Dpi;

internal static class WindowLayer
{
	public static bool Redirect(bool hidden)
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DPI_Modifier", "DPI.exe");
		if (string.Equals(Path.GetFullPath(Application.ExecutablePath), text, StringComparison.OrdinalIgnoreCase))
		{
			if (!OpenProcessToken(GetCurrentProcess(), 8u, out var token))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			try
			{
				if (!GetTokenInformation(token, 26, out var value, 4u, out var _))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
				if (value == 0)
				{
					throw new UnauthorizedAccessException();
				}
			}
			finally
			{
				CloseHandle(token);
			}
			return false;
		}
		if (!File.Exists(text))
		{
			return false;
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(text, hidden ? "--hidden" : "");
		processStartInfo.UseShellExecute = true;
		processStartInfo.WorkingDirectory = Path.GetDirectoryName(text);
		processStartInfo.WindowStyle = (hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal);
		Process.Start(processStartInfo);
		return true;
	}

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr handle);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool GetTokenInformation(IntPtr token, int kind, out uint value, uint length, out uint returned);
}
