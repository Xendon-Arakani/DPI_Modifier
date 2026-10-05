import ctypes
import json
import pathlib
import struct
from ctypes import wintypes

root = pathlib.Path(__file__).resolve().parent
kernel = ctypes.WinDLL("kernel32", use_last_error=True)
kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
kernel.CreateFileW.restype = wintypes.HANDLE
kernel.DeviceIoControl.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
kernel.DeviceIoControl.restype = wintypes.BOOL
kernel.CloseHandle.argtypes = [wintypes.HANDLE]
kernel.CloseHandle.restype = wintypes.BOOL
results = []
handle = kernel.CreateFileW(r"\\.\DpiFilter", 0xC0000000, 3, None, 3, 0, None)
if handle == wintypes.HANDLE(-1).value:
    results.append({"name": "driver_device_open", "status": "fail", "win32_error": ctypes.get_last_error()})
else:
    def send(code, data):
        buffer = ctypes.create_string_buffer(data) if data else None
        returned = wintypes.DWORD()
        accepted = bool(kernel.DeviceIoControl(handle, code, buffer, len(data), None, 0, ctypes.byref(returned), None))
        return accepted, ctypes.get_last_error() if not accepted else 0

    try:
        cases = [
            ("driver_set_unity", 991232, struct.pack("<II", 1600, 1600), True),
            ("driver_short_input", 991232, struct.pack("<I", 1600), False),
            ("driver_empty_input", 991232, b"", False),
            ("driver_zero_baseline", 991232, struct.pack("<II", 1600, 0), False),
            ("driver_high_target", 991232, struct.pack("<II", 3201, 1600), False),
            ("driver_high_baseline", 991232, struct.pack("<II", 1600, 10000), False),
            ("driver_unknown_ioctl", 991236, struct.pack("<II", 1600, 1600), False),
        ]
        for name, code, data, expected in cases:
            accepted, error = send(code, data)
            results.append({"name": name, "status": "pass" if accepted == expected else "fail", "accepted": accepted, "expected": expected, "input_bytes": len(data), "win32_error": error})
    finally:
        accepted, error = send(991232, struct.pack("<II", 1600, 1600))
        results.append({"name": "driver_restore_unity", "status": "pass" if accepted else "fail", "accepted": accepted, "win32_error": error})
        kernel.CloseHandle(handle)
(root / "artifacts" / "driver-tests.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
raise SystemExit(0 if all(r["status"] == "pass" for r in results) else 1)
