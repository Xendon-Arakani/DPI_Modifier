# Safety candidate validation

Downloads remain locked until these checks are recorded against the exact package
hash. Use a disposable Windows 10/11 x64 test machine with recovery access; never
use the development computer as an unattended reboot target.

- Fresh install with test signing, integrity bypass and boot debugging already off.
- Fresh install with Secure Boot enabled and Windows Memory Integrity enabled.
- Verify the Microsoft-signed rawaccel driver loads, `mouclass` remains registered,
  and all connected mice report no device errors after restart.
- Verify physical input scaling at base, lower and upper DPI values with the actual
  G HUB baseline unchanged. Verify tray exit restores the original configuration.
- Upgrade the withdrawn 0.1.0-progress.2 installation, with its original filter
  registered, then restart with the three flags off. Confirm no `DpiFilter` class
  entry remains and mouse input works.
- Remove the candidate, restart, and confirm normal input. Repeat install before
  that removal restart to ensure queued deletion cannot remove the new driver.
- With RawAccel already installed, confirm reuse and uninstall preserve it.
- Test missing runtime installation and a changed/tampered runtime download. Failure
  must occur before any mouse class registration changes.
- Test cleanup failures: remove the filter before the service/file, never restore
  the blocked legacy filter, and retain a signed service/file if detach fails.

Record OS build, Secure Boot/Memory Integrity state, package/driver hashes, device
error codes before and after boot, physical input results, and the uninstall result.
Static signature verification and mathematical tests alone do not establish a
successful clean boot or physical input behavior.
