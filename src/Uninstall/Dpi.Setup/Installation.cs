using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Dpi.Setup;

public sealed class Installation
{
	public int Format = 1;

	public string UserSid;

	public string DriverName;

	public string DriverHash;

	public string Thumbprint;

	public bool RootAdded;

	public bool TestModeChanged;

	public bool? PreviousTestMode;

	public static Installation Load()
	{
		Layout.CheckPath(Layout.Root);
		Installation installation = new JavaScriptSerializer().Deserialize<Installation>(File.ReadAllText(Layout.RecordPath));
		if (installation == null || installation.Format != 1 || !Regex.IsMatch(installation.DriverName ?? "", "^DpiFilter\\.[A-F0-9]{16}\\.sys$") || !Regex.IsMatch(installation.DriverHash ?? "", "^[A-F0-9]{64}$") || installation.DriverName != "DpiFilter." + installation.DriverHash.Substring(0, 16) + ".sys" || !Regex.IsMatch(installation.Thumbprint ?? "", "^[A-F0-9]{40}$") || (installation.TestModeChanged && installation.PreviousTestMode == true))
		{
			throw new InvalidOperationException("The installation record is invalid.");
		}
		using (Layout.OpenRun(installation.UserSid, write: false))
		{
			return installation;
		}
	}

	public void Save()
	{
		File.WriteAllText(Layout.RecordPath, new JavaScriptSerializer().Serialize(this));
	}
}
