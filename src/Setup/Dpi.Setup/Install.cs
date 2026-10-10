using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
namespace Dpi.Setup;
internal static class Install
{
    public static void Apply(string userSid)
    {
        Platform.Check();
        if (!Platform.IsAdministrator) throw new UnauthorizedAccessException("Administrator approval is required.");
        using (Layout.OpenRun(userSid, false)) { }
        var payload = new Payload();
        Layout.CheckPath(Layout.Root);
        Layout.CheckPath(Layout.Drivers);
        Layout.CheckPath(Layout.Shortcut);
        string checkedFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DPI_Verify_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(checkedFolder);
        try
        {
            string checkedDriver = Path.Combine(checkedFolder, "rawaccel.sys");
            payload.Write("rawaccel.sys", checkedDriver);
            Platform.VerifyDriver(checkedDriver);
            RuntimePrerequisite.Ensure();
        }
        finally { foreach (var file in Directory.GetFiles(checkedFolder)) File.Delete(file); Directory.Delete(checkedFolder); }
        if (File.Exists(Layout.RecordPath))
        {
            var previous = Installation.Load();
            if (previous.UserSid != userSid) throw new InvalidOperationException("Upgrade from the Windows account that installed DPI Modifier.");
            Removal.Apply(previous, true);
        }
        if (Directory.Exists(Layout.Root) || File.Exists(Layout.Root) || File.Exists(Layout.Shortcut))
            throw new InvalidOperationException("An unidentified DPI Modifier installation remains. Installation was stopped.");
        string[] originalFilters = Layout.Filters();
        if (originalFilters.Any(v => string.Equals(v, "DpiFilter", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An unidentified older mouse filter remains. Installation was stopped.");
        string driver = Path.Combine(Layout.Drivers, "rawaccel.sys");
        Layout.CheckPath(driver);
        bool existingService;
        using (var service = Registry.LocalMachine.OpenSubKey(Layout.Service))
        {
            existingService = service != null;
            if (existingService)
            {
                var image = Convert.ToString(service.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames));
                if (!string.Equals(image, "\\SystemRoot\\System32\\drivers\\rawaccel.sys", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Environment.ExpandEnvironmentVariables(image).Trim('"'), driver, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Another RawAccel driver is registered at a different location.");
                if (Convert.ToInt32(service.GetValue("Type", 0)) != 1 || Convert.ToInt32(service.GetValue("Start", 0)) != 3 ||
                    !originalFilters.Any(v => string.Equals(v, "rawaccel", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The existing RawAccel installation is incomplete. Installation was stopped.");
                Platform.VerifyDriver(driver);
            }
        }
        if (!existingService && (File.Exists(driver) || originalFilters.Any(v => string.Equals(v, "rawaccel", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("An unidentified RawAccel installation remains.");
        var record = new Installation { UserSid = userSid, DriverOwned = !existingService, Files = Payload.AppFiles.ToDictionary(v => v, v => payload.Hashes[v]) };
        bool rootCreated = false, driverWritten = false, serviceCreated = false, filterAdded = false, runAdded = false, uninstallAdded = false;
        try
        {
            Layout.CreateRoot(); rootCreated = true;
            foreach (var name in Payload.AppFiles) payload.Write(name, Path.Combine(Layout.Root, name));
            record.Save();
            if (!existingService)
            {
                driverWritten = true; payload.Write("rawaccel.sys", driver);
                Platform.VerifyDriver(driver);
                using (var service = Registry.LocalMachine.CreateSubKey(Layout.Service))
                {
                    serviceCreated = true;
                    service.SetValue("Type", 1, RegistryValueKind.DWord);
                    service.SetValue("Start", 3, RegistryValueKind.DWord);
                    service.SetValue("ErrorControl", 1, RegistryValueKind.DWord);
                    service.SetValue("ImagePath", "\\SystemRoot\\System32\\drivers\\rawaccel.sys", RegistryValueKind.ExpandString);
                    service.SetValue("DisplayName", "RawAccel");
                }
                filterAdded = true; Layout.SetFilters(Layout.AddFilter(originalFilters));
            }
            using (var run = Layout.OpenRun(userSid, true))
            {
                if (run.GetValue("DPI_Modifier") != null) throw new InvalidOperationException("An unidentified startup entry exists.");
                run.SetValue("DPI_Modifier", Layout.StartupValue); runAdded = true;
            }
            using (var uninstall = Registry.LocalMachine.CreateSubKey(Layout.Removal))
            {
                uninstallAdded = true;
                uninstall.SetValue("DisplayName", "DPI Modifier");
                uninstall.SetValue("DisplayVersion", "0.2.0-safety.1");
                uninstall.SetValue("InstallLocation", Layout.Root);
                uninstall.SetValue("DisplayIcon", Layout.ApplicationPath + ",0");
                uninstall.SetValue("UninstallString", "\"" + Layout.RemovalPath + "\"");
                uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
                uninstall.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            Layout.CreateShortcut(Layout.Shortcut);
        }
        catch (Exception error)
        {
            var cleanupErrors = new List<string>();
            Action<Action> attempt = action => { try { action(); } catch (Exception e) { cleanupErrors.Add(e.Message); } };
            bool detached = !filterAdded;
            if (filterAdded) attempt(() => { Layout.SetFilters(Layout.RemoveFilter(Layout.Filters())); detached = true; });
            // If detach fails, keep the verified service/file so input can still load.
            if (detached && serviceCreated) attempt(() => Registry.LocalMachine.DeleteSubKeyTree(Layout.Service, false));
            if (detached && driverWritten) attempt(() => File.Delete(driver));
            if (runAdded) attempt(() => { using var run = Layout.OpenRun(userSid, true); run.DeleteValue("DPI_Modifier", false); });
            if (uninstallAdded) attempt(() => Registry.LocalMachine.DeleteSubKeyTree(Layout.Removal, false));
            attempt(() => { if (File.Exists(Layout.Shortcut)) File.Delete(Layout.Shortcut); });
            if (rootCreated && detached) attempt(() => {
                foreach (var name in Payload.AppFiles.Concat(new[] { "installation.json" })) { string path = Path.Combine(Layout.Root, name); if (File.Exists(path)) File.Delete(path); }
                if (Directory.GetFileSystemEntries(Layout.Root).Length == 0) Directory.Delete(Layout.Root);
            });
            throw new InvalidOperationException(error.Message + (cleanupErrors.Count == 0 ? "" : "\r\nCleanup needs attention:\r\n" + string.Join("\r\n", cleanupErrors)));
        }
    }
}
