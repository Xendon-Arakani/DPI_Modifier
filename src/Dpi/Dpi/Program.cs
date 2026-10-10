using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Dpi;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		bool flag = Array.IndexOf(args, "--hidden") >= 0;
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "Local\\DPI_SingleInstance", out createdNew))
		{
			if (!createdNew)
			{
				return;
			}
			SetProcessDPIAware();
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			using MainForm mainForm = new MainForm();
			if (flag)
			{
				mainForm.StartHidden();
				using ApplicationContext context = new ApplicationContext(mainForm);
				Application.Run(context);
				return;
			}
			Application.Run(mainForm);
		}
	}

	[DllImport("user32.dll")]
	private static extern bool SetProcessDPIAware();
}
