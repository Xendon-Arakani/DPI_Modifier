using System;
using System.IO;
using System.Web.Script.Serialization;

namespace Dpi;

public sealed class AppState
{
	public int[] Values;

	public int Index = -1;

	public int BaseDpi = 1600;

	public int LowerRangeDpi = 550;

	public int UpperRangeDpi = 550;

	public int DurationSeconds = 105;

	public int CountdownSeconds = 3;

	public bool Automatic = true;

	public bool EnablePlus = true;

	public static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DPI", "state.json");

	public static AppState Load()
	{
		try
		{
			string statePath = StatePath;
			if (!File.Exists(statePath))
			{
				return new AppState();
			}
			JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
			AppState appState = javaScriptSerializer.Deserialize<AppState>(File.ReadAllText(statePath));
			return appState ?? new AppState();
		}
		catch
		{
			return new AppState();
		}
	}

	public bool Save()
	{
		string text = StatePath + ".tmp";
		try
		{
			string directoryName = Path.GetDirectoryName(StatePath);
			Directory.CreateDirectory(directoryName);
			JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
			File.WriteAllText(text, javaScriptSerializer.Serialize(this));
			if (File.Exists(StatePath))
			{
				File.Replace(text, StatePath, null);
			}
			else
			{
				File.Move(text, StatePath);
			}
			return true;
		}
		catch
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
			return false;
		}
	}
}
