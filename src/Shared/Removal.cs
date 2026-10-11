using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
namespace Dpi.Setup;
internal static class Removal
{
    public static void Validate(Installation record)
    {
        Layout.CheckPath(Layout.Root);
        string driver = Path.Combine(Layout.Drivers, record.DriverName);
        Layout.CheckPath(driver);
        if (File.Exists(driver) && Layout.HashFile(driver) != record.DriverHash)
            throw new InvalidDataException("The installed mouse driver has been modified. Removal was stopped.");
        string service = record.Format == 1 ? "DpiFilter" : "rawaccel";
        using (var key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + service))
        {
            if (key != null)
            {
                var image = Convert.ToString(key.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames));
                if (!string.Equals(image, "\\SystemRoot\\System32\\drivers\\" + record.DriverName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Environment.ExpandEnvironmentVariables(image).Trim('"'), driver, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The mouse driver registration does not match this installation.");
            }
        }
        foreach (var file in record.Files)
        {
            string path = Path.Combine(Layout.Root, file.Key);
            Layout.CheckPath(path);
            if (File.Exists(path) && Layout.HashFile(path) != file.Value)
                throw new InvalidDataException("An installed file has been modified: " + file.Key);
        }
        if (Directory.GetFiles(Layout.Root).Any(p => Path.GetFileName(p) != "installation.json" && !record.Files.ContainsKey(Path.GetFileName(p))) ||
            Directory.GetDirectories(Layout.Root).Length != 0)
            throw new InvalidDataException("The installation folder contains additional files. Removal was stopped.");
    }
    public static void Apply(Installation record, bool upgrade = false)
    {
        Validate(record);
        Platform.StopApplication(Layout.ApplicationPath, record.Format == 1);
        if (record.DriverOwned)
        {
            string name = record.Format == 1 ? "DpiFilter" : "rawaccel";
            MouseRegistration.Detach(new WindowsMouseRegistration(), name);
            string driver = Path.Combine(Layout.Drivers, record.DriverName);
            if (File.Exists(driver))
            {


                string retired = driver + ".removed-" + Guid.NewGuid().ToString("N");
                File.Move(driver, retired);
                Platform.QueueDelete(retired);
            }
        }
        using (var run = Layout.OpenRun(record.UserSid, true))
            if (Convert.ToString(run.GetValue("DPI_Modifier")) == Layout.StartupValue)
                run.DeleteValue("DPI_Modifier", false);
        Registry.LocalMachine.DeleteSubKeyTree(Layout.Removal, false);
        if (record.Format == 1 && record.RootAdded) Layout.RemoveRoot(record.Thumbprint);
        if (File.Exists(Layout.Shortcut)) File.Delete(Layout.Shortcut);
        string running = System.Reflection.Assembly.GetExecutingAssembly().Location;
        foreach (var file in record.Files)
        {
            string path = Path.Combine(Layout.Root, file.Key);
            if (!File.Exists(path)) continue;
            if (string.Equals(path, running, StringComparison.OrdinalIgnoreCase))
            {
                string retired = path + ".removed-" + Guid.NewGuid().ToString("N");
                File.Move(path, retired);
                Platform.QueueDelete(retired);
            }
            else File.Delete(path);
        }
        File.Delete(Layout.RecordPath);
        if (Directory.GetFileSystemEntries(Layout.Root).Length == 0) Directory.Delete(Layout.Root);

    }
}
