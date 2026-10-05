using System;
using System.Threading;
using System.Windows.Forms;

namespace Dpi.Setup;

internal static class MainEntry
{
	[STAThread]
	private static int Main(string[] args)
	{
		try
		{
			if (args.Length == 2 && args[0] == "--verify")
			{
				Platform.Check();
				new Payload().Verify(args[1]);
				return 0;
			}
			if (args.Length == 2 && args[0] == "--apply-install")
			{
				if (!Platform.IsAdministrator)
				{
					throw new UnauthorizedAccessException("Administrator approval is required.");
				}
				bool createdNew;
				using (new Mutex(initiallyOwned: true, "Global\\DPI_Modifier_Setup", out createdNew))
				{
					if (!createdNew)
					{
						throw new InvalidOperationException("Another DPI Modifier installation operation is running.");
					}
					Install.Apply(args[1]);
				}
				return 3010;
			}
			if (args.Length != 0)
			{
				throw new ArgumentException("Unsupported arguments.");
			}
			Platform.Check();
			Platform.SetProcessDPIAware();
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			using (SetupForm mainForm = new SetupForm())
			{
				Application.Run(mainForm);
			}
			return 0;
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "DPI Modifier", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return 1;
		}
	}
}
