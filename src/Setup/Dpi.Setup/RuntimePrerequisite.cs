using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using Microsoft.Win32;
namespace Dpi.Setup;
internal static class RuntimePrerequisite
{
    internal const string DownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    internal const string DownloadHash = "CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B";
    internal static bool IsAvailable()
    {
        using var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64");
        return key != null && Convert.ToInt32(key.GetValue("Installed", 0)) == 1 &&
            Version.TryParse(Convert.ToString(key.GetValue("Version", "")).TrimStart('v'), out var version) && version >= new Version(14, 29);
    }
    public static void Ensure()
    {
        if (IsAvailable()) return;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DPI_Runtime_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "VC_redist.x64.exe");
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var client = new WebClient()) client.DownloadFile(DownloadUrl, path);
            if (Layout.HashFile(path) != DownloadHash) throw new InvalidDataException("Microsoft's runtime download has changed. Get an updated DPI Modifier package.");
            Platform.VerifySignature(path);
            using var process = Process.Start(new ProcessStartInfo(path, "/install /passive /norestart") { UseShellExecute = false, CreateNoWindow = true });
            process.WaitForExit();
            if ((process.ExitCode != 0 && process.ExitCode != 3010) || !IsAvailable()) throw new InvalidOperationException("The Microsoft runtime could not be installed.");
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder); }
    }
}
