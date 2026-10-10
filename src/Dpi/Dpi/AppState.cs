using System;
using System.IO;
using System.Web.Script.Serialization;

namespace Dpi;

public sealed class AppState
{
	private readonly string storagePath;
	public AppState() { }
	internal AppState(string storagePath) => this.storagePath = storagePath;
	public int[] Values;

	public int Index = -1;

	public int BaseDpi = 1600;

	public int LowerRangeDpi = 550;

	public int UpperRangeDpi = 550;

	public int DurationSeconds = 105;

	public int CountdownSeconds = 3;

	public bool Automatic = true;

	public bool EnablePlus = true;

#if DEBUG
	public static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DPI.next", "state.json");
#else
	public static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DPI", "state.json");
#endif

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
		string destination = storagePath ?? StatePath;
		string text = destination + ".tmp";
		try
		{
			string directoryName = Path.GetDirectoryName(destination);
			Directory.CreateDirectory(directoryName);
			JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
			File.WriteAllText(text, javaScriptSerializer.Serialize(this));
			if (File.Exists(destination))
			{
				File.Replace(text, destination, null);
			}
			else
			{
				File.Move(text, destination);
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
