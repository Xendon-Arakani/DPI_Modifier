using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
namespace Dpi.Setup;
public sealed class Installation
{
    public const string SignedDriverHash = "8A62C4DEEF2774B43A7363B352EDA79897533A1080C9C26FFEFF0559E43358D7";
    public const string LegacyDriverHash = "61E949C6F1E25DCE05F5471EAABC5782E0BDCA92C0EF897C92FC4CA52D06B494";
    public int Format = 2;
    public string UserSid;
    public string DriverName = "rawaccel.sys";
    public string DriverHash = SignedDriverHash;
    public bool DriverOwned;
    public Dictionary<string, string> Files;
    // Read-only compatibility with the withdrawn installer; never change boot policy.
    public string Thumbprint;
    public bool RootAdded;
    public bool TestModeChanged;
    public bool? PreviousTestMode;
    public static Installation Load()
    {
        Layout.CheckPath(Layout.RecordPath);
        var record = new JavaScriptSerializer().Deserialize<Installation>(File.ReadAllText(Layout.RecordPath));
        if (record == null) throw new InvalidDataException("The installation record is missing.");
        if (record.Format == 1)
        {
            if (record.DriverHash != LegacyDriverHash || record.DriverName != "DpiFilter.61E949C6F1E25DCE.sys" ||
                record.Thumbprint != "9EDA884BC91CD4CAE62F0069E5E28B886BE4F635")
                throw new InvalidDataException("The older installation could not be identified safely.");
            record.DriverOwned = true;
            record.Files = new Dictionary<string, string> {
                {"DPI.exe", "EF0DC9301D76D04B8FA9F37E00BF15DBC3A1E7908E5866E56E4A255B513975DE"},
                {"Launch.exe", "11480E7ABF33E76ADEC36942C39B7C973BF850A3D7F5BABEA0AF6197693C336F"},
                {"Uninstall.exe", "FB33BBF4956F774DE4753DD990D67992420370D85C343AF24BD63310E12C1ED8"}
            };
        }
        else if (record.Format != 2 || record.DriverName != "rawaccel.sys" || record.DriverHash != SignedDriverHash || record.Files == null)
            throw new InvalidDataException("The installation record is invalid.");
        foreach (var file in record.Files)
            if (Path.GetFileName(file.Key) != file.Key || file.Key == "installation.json" ||
                !System.Text.RegularExpressions.Regex.IsMatch(file.Value ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("The installed file list is invalid.");
        using (Layout.OpenRun(record.UserSid, false)) return record;
    }
    public void Save() => File.WriteAllText(Layout.RecordPath, new JavaScriptSerializer().Serialize(this));
}
