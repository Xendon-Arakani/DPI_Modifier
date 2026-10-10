using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
namespace Dpi.Setup;
internal sealed class Payload
{
    internal static readonly string[] AppFiles = { "DPI.exe", "DPI.exe.config", "Launch.exe", "Launch.exe.config", "Uninstall.exe", "Uninstall.exe.config", "wrapper.dll", "Newtonsoft.Json.dll", "RawAccel-LICENSE.txt", "Newtonsoft.Json-LICENSE.txt" };
    private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
    public readonly Dictionary<string, string> Hashes;
    public Payload()
    {
        Hashes = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(Read("hashes.json")));
        if (Hashes == null || Hashes.Count != AppFiles.Length + 1) throw new InvalidDataException("Invalid package manifest.");
        foreach (string name in System.Linq.Enumerable.Concat(AppFiles, new[] { "rawaccel.sys" }))
        {
            byte[] bytes = Read(name);
            if (!Hashes.TryGetValue(name, out var hash) || Layout.Hash(bytes) != hash) throw new InvalidDataException("Damaged package: " + name);
            files.Add(name, bytes);
        }
        if (Hashes["rawaccel.sys"] != Installation.SignedDriverHash) throw new InvalidDataException("Unapproved mouse driver.");
    }
    private static byte[] Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dpi.Payload." + name);
        if (stream == null) throw new InvalidDataException("Missing packaged file: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
    public void Write(string name, string path)
    {
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { byte[] bytes = files[name]; file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        if (Layout.HashFile(path) != Hashes[name]) throw new IOException("File verification failed: " + name);
    }
    public void Verify(string directory)
    {
        string path = Path.GetFullPath(directory);
        Layout.CheckPath(path);
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Use a new verification directory.");
        Directory.CreateDirectory(path);
        foreach (string name in files.Keys) Write(name, Path.Combine(path, name));
        Platform.VerifyDriver(Path.Combine(path, "rawaccel.sys"));
        File.WriteAllText(Path.Combine(path, "verify.json"), new JavaScriptSerializer().Serialize(new {
            Success = true, DriverHash = Installation.SignedDriverHash, MicrosoftSigned = true,
            TestSigningRequired = false, BootSettingsChanged = false, Files = Hashes
        }));
    }
}
