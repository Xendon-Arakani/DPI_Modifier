using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Dpi;

internal sealed class MainForm : Form
{
	private const int WM_HOTKEY = 786;

	private const int HOTKEY_NEXT = 1;

	private const int HOTKEY_RESET = 2;

	private const int HOTKEY_TIMING = 3;

	private const uint MOD_NOREPEAT = 16384u;

	private const int VK_ADD = 107;

	private const int VK_DIVIDE = 111;

	private const int VK_MULTIPLY = 106;

	private static readonly Color Background = Color.FromArgb(15, 17, 22);

	private static readonly Color Card = Color.FromArgb(25, 28, 35);

	private static readonly Color CardLight = Color.FromArgb(35, 39, 48);

	private static readonly Color TextMain = Color.FromArgb(241, 244, 249);

	private static readonly Color TextMuted = Color.FromArgb(150, 158, 173);

	private static readonly Color Accent = Color.FromArgb(255, 112, 67);

	private static readonly Color Green = Color.FromArgb(60, 210, 143);

	private readonly AppState state;

	private readonly SeriesSource seriesSource;

	private readonly KernelMouseController mouse;

	private readonly DeadlineClock clock = new DeadlineClock();

	private readonly Stopwatch elapsed = Stopwatch.StartNew();

	private readonly Timer wakeTimer = new Timer();

	private readonly Icon windowIcon;

	private readonly Icon trayIcon;

	private CountdownOverlay countdown;

	private TextBox dpiInput;

	private TextBox baseInput;

	private TextBox lowerInput;

	private TextBox upperInput;

	private TextBox durationInput;

	private TextBox countdownInput;

	private CheckBox modeToggle;

	private CheckBox plusToggle;

	private Button nextButton;

	private Button timingButton;

	private bool updatingControls;

	private Label indexLabel;

	private Label kindLabel;

	private FlowLayoutPanel entriesPanel;

	private readonly ValueCard[] entries = new ValueCard[9];

	private NotifyIcon tray;

	private bool allowExit;

	private int overrideDpi = -1;

	private int CurrentDpi
	{
		get
		{
			if (overrideDpi < 0)
			{
				if (state.Index >= 0)
				{
					return state.Values[state.Index];
				}
				return state.BaseDpi;
			}
			return overrideDpi;
		}
	}

	public MainForm()
	{
		using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("Dpi.Icon"))
		{
			windowIcon = new Icon(stream, SystemInformation.IconSize);
		}
		trayIcon = new Icon(windowIcon, SystemInformation.SmallIconSize);
		base.Icon = windowIcon;
		state = AppState.Load();
		seriesSource = new SeriesSource();
		bool flag = false;
		if (!SeriesSource.IsConfigurationValid(state.BaseDpi, state.LowerRangeDpi, state.UpperRangeDpi))
		{
			state.BaseDpi = 1600;
			state.LowerRangeDpi = 550;
			state.UpperRangeDpi = 550;
			flag = true;
		}
		if (!SeriesSource.IsValid(state.Values, state.BaseDpi, state.LowerRangeDpi, state.UpperRangeDpi))
		{
			state.Values = seriesSource.Create(state.BaseDpi, state.LowerRangeDpi, state.UpperRangeDpi);
			state.Index = -1;
			flag = true;
		}
		if (!SeriesSource.IsTimingConfigurationValid(state.DurationSeconds, state.CountdownSeconds))
		{
			state.DurationSeconds = 105;
			state.CountdownSeconds = 3;
			flag = true;
		}
		state.Index = Math.Max(-1, Math.Min(8, state.Index));
		if (flag)
		{
			state.Save();
		}
		mouse = new KernelMouseController();
		wakeTimer.Tick += delegate
		{
			WakeTiming();
		};
		BuildWindow();
		BuildTray();
		UpdateSettingsFields();
		UpdateTimingFields();
		UpdateView();
		UpdateControls();
		base.Shown += delegate
		{
			ApplyCurrent();
		};
	}

	private void BuildWindow()
	{
		Text = "DPI";
		BackColor = Background;
		ForeColor = TextMain;
		Font = new Font("Segoe UI", 10f);
		base.AutoScaleMode = AutoScaleMode.None;
		base.ClientSize = new Size(920, 510);
		MinimumSize = new Size(920, 510);
		base.StartPosition = FormStartPosition.CenterScreen;
		base.FormBorderStyle = FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Padding = new Padding(30, 20, 30, 20);
		tableLayoutPanel.RowCount = 6;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 106f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		base.Controls.Add(tableLayoutPanel);
		tableLayoutPanel.Controls.Add(BuildHeader(), 0, 0);
		tableLayoutPanel.Controls.Add(BuildValuePanel(), 0, 1);
		tableLayoutPanel.Controls.Add(BuildEntries(), 0, 2);
		tableLayoutPanel.Controls.Add(BuildSettings(), 0, 3);
		tableLayoutPanel.Controls.Add(BuildTiming(), 0, 4);
		tableLayoutPanel.Controls.Add(BuildActions(), 0, 5);
		using Graphics graphics = CreateGraphics();
		float num = graphics.DpiX / 96f;
		if (num != 1f)
		{
			Scale(new SizeF(num, num));
		}
	}

	private Control BuildHeader()
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		Panel panel2 = panel;
		Label label = new Label();
		label.AutoSize = true;
		label.Text = "DPI";
		label.ForeColor = TextMain;
		label.Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold);
		label.Location = new Point(0, 0);
		Label value = label;
		panel2.Controls.Add(value);
		return panel2;
	}

	private Control BuildValuePanel()
	{
		RoundedPanel roundedPanel = new RoundedPanel();
		roundedPanel.Dock = DockStyle.Fill;
		roundedPanel.BackColor = Card;
		roundedPanel.Radius = 18;
		roundedPanel.Margin = new Padding(0, 0, 0, 14);
		RoundedPanel roundedPanel2 = roundedPanel;
		indexLabel = MakeLabel("ENTRY 1 OF 9", 400, TextMuted, new Point(24, 12));
		indexLabel.Font = new Font("Segoe UI Semibold", 9.5f);
		kindLabel = MakeLabel("BASE DPI", 400, Green, new Point(300, 54));
		kindLabel.Font = new Font("Segoe UI Semibold", 10f);
		dpiInput = MakeInput(new Point(24, 35), 205);
		dpiInput.Font = new Font("Segoe UI Semibold", 26f, FontStyle.Bold);
		dpiInput.BackColor = Card;
		dpiInput.BorderStyle = BorderStyle.None;
		dpiInput.Leave += delegate
		{
			CommitValue();
		};
		dpiInput.KeyDown += delegate(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				CommitValue();
				e.SuppressKeyPress = true;
			}
		};
		Label label = MakeLabel("DPI", 60, TextMuted, new Point(236, 54));
		label.Font = new Font("Segoe UI Semibold", 12f);
		roundedPanel2.Controls.Add(indexLabel);
		roundedPanel2.Controls.Add(kindLabel);
		roundedPanel2.Controls.Add(dpiInput);
		roundedPanel2.Controls.Add(label);
		return roundedPanel2;
	}

	private Control BuildEntries()
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		Panel panel2 = panel;
		Label label = MakeLabel("VALUES", 100, TextMuted, new Point(0, 0));
		label.Font = new Font("Segoe UI Semibold", 9f);
		panel2.Controls.Add(label);
		entriesPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 76,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			BackColor = Background
		};
		panel2.Controls.Add(entriesPanel);
		for (int i = 0; i < entries.Length; i++)
		{
			entries[i] = new ValueCard
			{
				Size = new Size(86, 70),
				Margin = new Padding(0, 0, 7, 0),
				Ordinal = i + 1
			};
			entriesPanel.Controls.Add(entries[i]);
		}
		return panel2;
	}

	private Control BuildSettings()
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		Panel panel2 = panel;
		panel2.Controls.Add(MakeLabel("BASE DPI", 200, TextMuted, new Point(0, 0)));
		panel2.Controls.Add(MakeLabel("LOWER RANGE DPI", 200, TextMuted, new Point(225, 0)));
		panel2.Controls.Add(MakeLabel("UPPER RANGE DPI", 200, TextMuted, new Point(450, 0)));
		baseInput = MakeInput(new Point(0, 25), 190);
		lowerInput = MakeInput(new Point(225, 25), 190);
		upperInput = MakeInput(new Point(450, 25), 190);
		TextBox[] array = new TextBox[3] { baseInput, lowerInput, upperInput };
		foreach (TextBox textBox in array)
		{
			textBox.Leave += delegate
			{
				CommitSettings();
			};
			textBox.KeyDown += delegate(object sender, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Return)
				{
					CommitSettings();
					e.SuppressKeyPress = true;
				}
			};
			panel2.Controls.Add(textBox);
		}
		return panel2;
	}

	private static TextBox MakeInput(Point location, int width)
	{
		TextBox textBox = new TextBox();
		textBox.Location = location;
		textBox.Width = width;
		textBox.BackColor = CardLight;
		textBox.ForeColor = TextMain;
		textBox.BorderStyle = BorderStyle.FixedSingle;
		textBox.Font = new Font("Segoe UI", 11f);
		textBox.MaxLength = 5;
		return textBox;
	}

	private Control BuildTiming()
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		Panel panel2 = panel;
		panel2.Controls.Add(MakeLabel("DURATION SECONDS", 200, TextMuted, new Point(0, 0)));
		panel2.Controls.Add(MakeLabel("COUNTDOWN SECONDS", 200, TextMuted, new Point(225, 0)));
		durationInput = MakeInput(new Point(0, 25), 190);
		countdownInput = MakeInput(new Point(225, 25), 190);
		TextBox[] array = new TextBox[2] { durationInput, countdownInput };
		foreach (TextBox textBox in array)
		{
			textBox.Leave += delegate
			{
				CommitTiming();
			};
			textBox.KeyDown += delegate(object sender, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Return)
				{
					CommitTiming();
					e.SuppressKeyPress = true;
				}
			};
			panel2.Controls.Add(textBox);
		}
		modeToggle = new CheckBox
		{
			Location = new Point(675, 0),
			AutoSize = true,
			Text = "MANUAL"
		};
		plusToggle = new CheckBox
		{
			Location = new Point(675, 30),
			Size = new Size(20, 25),
			Text = ""
		};
		modeToggle.CheckedChanged += delegate
		{
			CommitMode();
		};
		plusToggle.CheckedChanged += delegate
		{
			CommitMode();
		};
		panel2.Controls.Add(modeToggle);
		panel2.Controls.Add(plusToggle);
		panel2.Controls.Add(MakeLabel("NUM +", 100, TextMuted, new Point(697, 30)));
		return panel2;
	}

	private Control BuildActions()
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		Panel panel2 = panel;
		nextButton = MakeButton("NEXT     NUM +", Accent, Color.White, new Size(270, 52));
		nextButton.Location = new Point(0, 8);
		nextButton.Click += delegate
		{
			RequestNext();
		};
		Button button = MakeButton("RESET     NUM /", CardLight, TextMain, new Size(250, 52));
		button.Location = new Point(285, 8);
		button.Click += delegate
		{
			ResetValues();
		};
		panel2.Controls.Add(nextButton);
		panel2.Controls.Add(button);
		timingButton = MakeButton("START     NUM *", CardLight, TextMain, new Size(270, 52));
		timingButton.Location = new Point(550, 8);
		timingButton.Click += delegate
		{
			ToggleTiming();
		};
		panel2.Controls.Add(timingButton);
		return panel2;
	}

	private void BuildTray()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		contextMenuStrip.Items.Add("Open", null, delegate
		{
			RestoreWindow();
		});
		contextMenuStrip.Items.Add("Exit", null, delegate
		{
			allowExit = true;
			Close();
		});
		tray = new NotifyIcon
		{
			Icon = trayIcon,
			Text = "DPI",
			Visible = true,
			ContextMenuStrip = contextMenuStrip
		};
		tray.DoubleClick += delegate
		{
			RestoreWindow();
		};
		base.Resize += delegate
		{
			if (base.WindowState == FormWindowState.Minimized)
			{
				HideToTray();
			}
		};
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		RegisterHotKey(base.Handle, 2, 16384u, 111);
		UpdateHotkeys();
	}

	protected override void OnHandleDestroyed(EventArgs e)
	{
		UnregisterHotKey(base.Handle, 1);
		UnregisterHotKey(base.Handle, 2);
		UnregisterHotKey(base.Handle, 3);
		base.OnHandleDestroyed(e);
	}

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == 786)
		{
			int num = message.WParam.ToInt32();
			if (num == 1 && !state.Automatic && state.EnablePlus)
			{
				RequestNext();
			}
			else
			{
				switch (num)
				{
				case 2:
					ResetValues();
					break;
				case 3:
					if (state.Automatic)
					{
						ToggleTiming();
					}
					break;
				}
			}
		}
		base.WndProc(ref message);
	}

	private bool AdvanceIndex()
	{
		int[] values = state.Values;
		int index = state.Index;
		int currentDpi = CurrentDpi;
		int[] array = state.Values;
		int num;
		if (state.Index >= 8)
		{
			array = seriesSource.Create(state.BaseDpi, state.LowerRangeDpi, state.UpperRangeDpi);
			num = 0;
		}
		else
		{
			num = state.Index + 1;
		}
		if (!mouse.Apply(array[num], state.BaseDpi))
		{
			return false;
		}
		long elapsedMilliseconds = elapsed.ElapsedMilliseconds;
		state.Values = array;
		state.Index = num;
		if (!state.Save())
		{
			state.Values = values;
			state.Index = index;
			mouse.Apply(currentDpi, state.BaseDpi);
			return false;
		}
		overrideDpi = -1;
		if (clock.IsSet)
		{
			clock.Start(state.DurationSeconds * 1000, elapsedMilliseconds, clock.IsRunning);
		}
		UpdateView();
		return true;
	}

	private void RequestNext()
	{
		if (!state.Automatic && state.EnablePlus)
		{
			AdvanceIndex();
		}
	}

	private void ResetValues()
	{
		int[] values = state.Values;
		int index = state.Index;
		int currentDpi = CurrentDpi;
		int[] values2 = seriesSource.Create(state.BaseDpi, state.LowerRangeDpi, state.UpperRangeDpi);
		if (mouse.Apply(state.BaseDpi, state.BaseDpi))
		{
			state.Values = values2;
			state.Index = -1;
			if (!state.Save())
			{
				state.Values = values;
				state.Index = index;
				mouse.Apply(currentDpi, state.BaseDpi);
			}
			else
			{
				overrideDpi = -1;
				StopTiming();
				UpdateView();
			}
		}
	}

	private void ApplyCurrent()
	{
		mouse.Apply(CurrentDpi, state.BaseDpi);
	}

	private bool ApplyOverride(int target)
	{
		if (!mouse.Apply(target, state.BaseDpi))
		{
			return false;
		}
		StopTiming();
		overrideDpi = target;
		UpdateView();
		return true;
	}

	private void CommitValue()
	{
		if (!int.TryParse(dpiInput.Text, out var result) || result == CurrentDpi || !ApplyOverride(result))
		{
			dpiInput.Text = CurrentDpi.ToString();
		}
	}

	private void UpdateSettingsFields()
	{
		baseInput.Text = state.BaseDpi.ToString();
		lowerInput.Text = state.LowerRangeDpi.ToString();
		upperInput.Text = state.UpperRangeDpi.ToString();
	}

	private void CommitSettings()
	{
		if (!int.TryParse(baseInput.Text, out var result) || result < 1 || result > 9999 || !int.TryParse(lowerInput.Text, out var result2) || result2 < 0 || !int.TryParse(upperInput.Text, out var result3) || result3 < 0)
		{
			UpdateSettingsFields();
			return;
		}
		result2 = Math.Min(result2, result);
		result3 = Math.Min(result3, result);
		if (!SeriesSource.IsConfigurationValid(result, result2, result3) || (result == state.BaseDpi && result2 == state.LowerRangeDpi && result3 == state.UpperRangeDpi))
		{
			UpdateSettingsFields();
			return;
		}
		int baseDpi = state.BaseDpi;
		int lowerRangeDpi = state.LowerRangeDpi;
		int upperRangeDpi = state.UpperRangeDpi;
		int currentDpi = CurrentDpi;
		int[] values = state.Values;
		int index = state.Index;
		int[] values2 = seriesSource.Create(result, result2, result3);
		if (!mouse.Apply(result, result))
		{
			UpdateSettingsFields();
			return;
		}
		state.BaseDpi = result;
		state.LowerRangeDpi = result2;
		state.UpperRangeDpi = result3;
		state.Values = values2;
		state.Index = -1;
		if (!state.Save())
		{
			state.BaseDpi = baseDpi;
			state.LowerRangeDpi = lowerRangeDpi;
			state.UpperRangeDpi = upperRangeDpi;
			state.Values = values;
			state.Index = index;
			mouse.Apply(currentDpi, baseDpi);
		}
		else
		{
			overrideDpi = -1;
			StopTiming();
		}
		UpdateSettingsFields();
		UpdateView();
	}

	private void UpdateView()
	{
		int currentDpi = CurrentDpi;
		bool flag = currentDpi == state.BaseDpi;
		dpiInput.Text = currentDpi.ToString();
		kindLabel.Text = ((overrideDpi >= 0) ? "OVERRIDE DPI" : ((state.Index < 0) ? "BASE DPI" : (flag ? "BASE DPI" : "DPI")));
		kindLabel.ForeColor = (flag ? Green : Accent);
		indexLabel.Text = ((overrideDpi >= 0) ? "OVERRIDE" : ((state.Index < 0) ? "READY" : ("ENTRY " + (state.Index + 1) + " OF 9")));
		for (int i = 0; i < entries.Length; i++)
		{
			ValueCard valueCard = entries[i];
			valueCard.Dpi = state.Values[i];
			valueCard.IsSelected = overrideDpi < 0 && i == state.Index;
			valueCard.IsBefore = i < state.Index;
			valueCard.IsBase = state.Values[i] == state.BaseDpi;
			valueCard.Invalidate();
		}
	}

	private void UpdateTimingFields()
	{
		durationInput.Text = state.DurationSeconds.ToString();
		countdownInput.Text = state.CountdownSeconds.ToString();
	}

	private void CommitTiming()
	{
		if (!int.TryParse(durationInput.Text, out var result) || !int.TryParse(countdownInput.Text, out var result2) || !SeriesSource.IsTimingConfigurationValid(result, result2) || (result == state.DurationSeconds && result2 == state.CountdownSeconds))
		{
			UpdateTimingFields();
			return;
		}
		int durationSeconds = state.DurationSeconds;
		int countdownSeconds = state.CountdownSeconds;
		state.DurationSeconds = result;
		state.CountdownSeconds = result2;
		if (!state.Save())
		{
			state.DurationSeconds = durationSeconds;
			state.CountdownSeconds = countdownSeconds;
		}
		else
		{
			StopTiming();
		}
		UpdateTimingFields();
		UpdateView();
	}

	private void CommitMode()
	{
		if (updatingControls)
		{
			return;
		}
		bool automatic = state.Automatic;
		bool enablePlus = state.EnablePlus;
		bool flag = !modeToggle.Checked;
		bool flag2 = (state.Automatic ? state.EnablePlus : plusToggle.Checked);
		if (flag != automatic || flag2 != enablePlus)
		{
			state.Automatic = flag;
			state.EnablePlus = flag2;
			if (!state.Save())
			{
				state.Automatic = automatic;
				state.EnablePlus = enablePlus;
			}
			else if (flag != automatic || (!flag2 && enablePlus))
			{
				StopTiming();
			}
			UpdateControls();
			UpdateHotkeys();
		}
	}

	private void UpdateControls()
	{
		updatingControls = true;
		modeToggle.Checked = !state.Automatic;
		modeToggle.Text = (state.Automatic ? "AUTO" : "MANUAL");
		plusToggle.Enabled = !state.Automatic;
		plusToggle.Checked = !state.Automatic && state.EnablePlus;
		nextButton.Enabled = !state.Automatic && state.EnablePlus;
		nextButton.BackColor = (nextButton.Enabled ? Accent : CardLight);
		timingButton.Enabled = state.Automatic;
		timingButton.Text = (clock.IsRunning ? "PAUSE" : (clock.IsSet ? "RESUME" : "START")) + "     NUM *";
		updatingControls = false;
	}

	private void UpdateHotkeys()
	{
		if (base.IsHandleCreated)
		{
			UnregisterHotKey(base.Handle, 1);
			UnregisterHotKey(base.Handle, 3);
			if (!state.Automatic && state.EnablePlus)
			{
				RegisterHotKey(base.Handle, 1, 16384u, 107);
			}
			if (state.Automatic)
			{
				RegisterHotKey(base.Handle, 3, 16384u, 106);
			}
		}
	}

	private void ToggleTiming()
	{
		if (!state.Automatic)
		{
			return;
		}
		if (clock.IsRunning)
		{
			clock.Pause(elapsed.ElapsedMilliseconds);
			wakeTimer.Stop();
			if (countdown != null)
			{
				countdown.Hide();
			}
		}
		else
		{
			if (clock.IsSet)
			{
				clock.Resume(elapsed.ElapsedMilliseconds);
			}
			else
			{
				clock.Start((state.Index < 0 || overrideDpi >= 0) ? (state.CountdownSeconds * 1000) : (state.DurationSeconds * 1000), elapsed.ElapsedMilliseconds, running: true);
			}
			WakeTiming();
		}
		UpdateControls();
	}

	private void StopTiming()
	{
		wakeTimer.Stop();
		clock.Clear();
		if (countdown != null)
		{
			countdown.Hide();
		}
		UpdateControls();
	}

	private void WakeTiming()
	{
		wakeTimer.Stop();
		if (!clock.IsRunning || !state.Automatic)
		{
			return;
		}
		long elapsedMilliseconds = elapsed.ElapsedMilliseconds;
		if (clock.Remaining(elapsedMilliseconds) == 0)
		{
			if (countdown != null)
			{
				countdown.Hide();
			}
			if (!AdvanceIndex())
			{
				StopTiming();
				return;
			}
			elapsedMilliseconds = elapsed.ElapsedMilliseconds;
		}
		int num = clock.Remaining(elapsedMilliseconds);
		int num2 = state.CountdownSeconds * 1000;
		if (num2 > 0 && num > 0 && num <= num2)
		{
			try
			{
				if (countdown == null)
				{
					countdown = new CountdownOverlay();
				}
				int colorPhase = (999 - (num - 1) % 1000) * 3 / 1000;
				if (!countdown.Display((num + 999) / 1000, colorPhase))
				{
					StopTiming();
					return;
				}
			}
			catch
			{
				StopTiming();
				return;
			}
		}
		else if (countdown != null)
		{
			countdown.Hide();
		}
		wakeTimer.Interval = clock.NextDelay(num2, elapsed.ElapsedMilliseconds);
		wakeTimer.Start();
	}

	private void HideToTray()
	{
		base.ShowInTaskbar = false;
		Hide();
	}

	internal void StartHidden()
	{
		HideToTray();
		ApplyCurrent();
	}

	private void RestoreWindow()
	{
		base.ShowInTaskbar = true;
		Show();
		base.WindowState = FormWindowState.Normal;
		Activate();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (!allowExit && e.CloseReason != CloseReason.WindowsShutDown)
		{
			e.Cancel = true;
			HideToTray();
		}
		else
		{
			StopTiming();
			base.OnFormClosing(e);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			wakeTimer.Dispose();
			if (countdown != null)
			{
				countdown.Dispose();
			}
			if (mouse != null)
			{
				mouse.Dispose();
			}
			if (tray != null)
			{
				tray.Dispose();
			}
			trayIcon.Dispose();
			windowIcon.Dispose();
		}
		base.Dispose(disposing);
	}

	private static Label MakeLabel(string text, int width, Color color, Point location)
	{
		Label label = new Label();
		label.Text = text;
		label.AutoSize = false;
		label.Width = width;
		label.Height = 25;
		label.ForeColor = color;
		label.BackColor = Color.Transparent;
		label.Location = location;
		return label;
	}

	private static Button MakeButton(string text, Color back, Color fore, Size size)
	{
		Button button = new Button();
		button.Text = text;
		button.Size = size;
		button.BackColor = back;
		button.ForeColor = fore;
		button.FlatStyle = FlatStyle.Flat;
		button.Cursor = Cursors.Hand;
		button.Font = new Font("Segoe UI Semibold", 10f);
		Button button2 = button;
		button2.FlatAppearance.BorderSize = 0;
		button2.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.08f);
		return button2;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, int key);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(IntPtr window, int id);
}
