using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Dpi.Setup;

internal sealed class SetupForm : Form
{
	private readonly Label permission = new Label();

	private readonly Label recovery = new Label();

	private readonly Label status = new Label();

	private readonly Button action = new Button();

	private readonly Icon icon;

	private bool busy;

	private bool finished;

	public SetupForm()
	{
		Text = "Remove DPI Modifier";
		permission.Text = "Your Windows security settings will be preserved.";
		Installation.Load();
		action.Text = "Remove";
		status.Text = "Restart required. Saved DPI settings will be kept.";
		BackColor = Color.FromArgb(15, 17, 22);
		ForeColor = Color.FromArgb(241, 244, 249);
		Font = new Font("Segoe UI", 10f);
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.ClientSize = new Size(570, 270);
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
		base.Icon = icon;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(24),
			RowCount = 5,
			ColumnCount = 1
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		base.Controls.Add(tableLayoutPanel);
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "DPI Modifier",
			Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
			Dock = DockStyle.Fill
		}, 0, 0);
		permission.Dock = DockStyle.Fill;
        permission.Enabled = true;
		recovery.Dock = DockStyle.Fill;
		tableLayoutPanel.Controls.Add(permission, 0, 1);
		status.Dock = DockStyle.Fill;
		tableLayoutPanel.Controls.Add(status, 0, 3);
		action.Dock = DockStyle.Fill;
		action.FlatStyle = FlatStyle.Flat;
		action.FlatAppearance.BorderSize = 0;
		action.BackColor = Color.FromArgb(255, 112, 67);
		action.ForeColor = Color.White;
		Button button = action;
		EventHandler value = delegate
		{
			RunOperation();
		};
		button.Click += value;
		tableLayoutPanel.Controls.Add(action, 0, 4);
		base.FormClosing += delegate(object sender, FormClosingEventArgs e)
		{
			if (busy)
			{
				e.Cancel = true;
			}
		};
	}

	private void RunOperation()
	{
		if (finished)
		{
			Close();
			return;
		}
		try
		{
			string arguments = "--apply-remove";
			busy = true;
			Button button = action;
			Label checkBox = permission;
			bool flag = (recovery.Enabled = false);
			bool enabled = (checkBox.Enabled = flag);
			button.Enabled = enabled;
			status.Text = "Waiting for administrator approval...";
			BackgroundWorker worker = new BackgroundWorker();
			worker.DoWork += delegate(object sender, DoWorkEventArgs e)
			{
				using Process process = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, arguments)
				{
					Verb = "runas",
					UseShellExecute = true,
					WorkingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
				});
				process.WaitForExit();
				e.Result = process.ExitCode;
			};
			worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
			{
				busy = false;
				Label checkBox2 = permission;
				bool enabled2 = (recovery.Enabled = true);
				checkBox2.Enabled = enabled2;
				action.Enabled = true;
				if (e.Error != null)
				{
					status.Text = ((e.Error is Win32Exception && ((Win32Exception)e.Error).NativeErrorCode == 1223) ? "Approval canceled." : e.Error.Message);
				}
				else if ((int)e.Result == 3010)
				{
					finished = true;
					status.Text = "Complete. Restart Windows.";
					action.Text = "Close";
				}
				else
				{
					status.Text = "Not completed. No restart requested.";
				}
				worker.Dispose();
			};
			worker.RunWorkerAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing && icon != null)
		{
			icon.Dispose();
		}
	}
}
