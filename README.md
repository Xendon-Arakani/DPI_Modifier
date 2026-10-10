# DPI Modifier

Downloads are paused while the signed-driver safety candidate is validated. The old
`0.1.0-progress.2` installer is withdrawn: its test-signed `DpiFilter` mouse class
filter can prevent mouse devices from starting after test signing is turned off.

The `0.2.0-safety.1` candidate uses the unmodified Microsoft-signed RawAccel 1.7.1
driver. The app applies constant input scaling (`target DPI / physical mouse DPI`),
with acceleration disabled. Set Base DPI to your mouse's actual DPI in G HUB.
Driver writes take one second; the window stays responsive while applying them.
Exit from the tray menu restores the previous RawAccel configuration.

Setup bundles the signed driver and app dependencies. If the Visual C++ runtime
is missing, setup downloads and verifies Microsoft's installer. Setup requires
administrator approval and a manual restart. It does not change test signing,
integrity checks, debugging, Secure Boot, or other boot settings, and it does not
add a signing certificate. The app runs as your normal Windows user.

The upgrade path recognizes the original installer, detaches `DpiFilter` before
removing its service, preserves `mouclass` and other filters, and installs the signed
replacement. A failed cleanup never restores the blocked legacy filter. An existing
matching RawAccel installation is reused and left installed when DPI Modifier is
removed. Avoid using both applications to change driver settings simultaneously.

If you still have the old installer on another laptop, remove that old installation
and restart **before** turning off test signing. Do not use the withdrawn installer
again. Changing those boot options while its old filter is registered can repeat
the loss of mouse input. The candidate is not yet approved for installation.

## Build and verify

Use the .NET SDK pinned by `global.json` on Windows x64:

```powershell
.\tests\Run-Tests.ps1
.\Build-Release.ps1
```

The build compiles the app, launcher and uninstaller first, generates the embedded
payload from those exact outputs, then compiles and extracts/verifies setup. It
does not reuse the original prebuilt app or driver. The source package includes
the hash-pinned, unmodified upstream binaries and their license notices.

Automated tests cover actual upstream scaling math, invalid input, original-config
restoration, filter cleanup ordering/failures, signature/tamper checks, and UI
responsiveness with an injected driver. They do not install a driver, change boot
settings, or restart the development machine. A clean install/upgrade/reboot test
with all three security flags off and a physical mouse is still required before
downloads can reopen. See [validation checklist](docs/VALIDATION.md).

RawAccel and its wrapper are MIT licensed; attribution is in
`vendor/RawAccel-1.7.1/LICENSE`. Newtonsoft.Json is MIT licensed; its notice is
included beside it. The DPI Modifier application's existing ownership is unchanged.
