using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dpi;

internal sealed class KernelMouseController : IDisposable
{
	private struct DriverState
	{
		public uint TargetDpi;

		public uint BaseDpi;
	}

	private const uint GenericRead = 2147483648u;

	private const uint GenericWrite = 1073741824u;

	private const uint OpenExisting = 3u;

	private const uint IoctlSetState = 991232u;

	private SafeFileHandle handle;

	public bool Apply(int target, int baseline)
	{
		if (baseline < 1 || baseline >= 10000 || target < 0 || target > baseline * 2)
		{
			return false;
		}
		if (handle == null)
		{
			handle = CreateFile("\\\\.\\DpiFilter", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
		}
		if (handle.IsInvalid)
		{
			Dispose();
			return false;
		}
		DriverState input = new DriverState
		{
			TargetDpi = (uint)target,
			BaseDpi = (uint)baseline
		};
		uint returned;
		bool flag = DeviceIoControl(handle, 991232u, ref input, 8u, IntPtr.Zero, 0u, out returned, IntPtr.Zero);
		if (!flag)
		{
			Dispose();
		}
		return flag;
	}

	public void Dispose()
	{
		if (handle != null)
		{
			handle.Dispose();
		}
		handle = null;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, ref DriverState input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
