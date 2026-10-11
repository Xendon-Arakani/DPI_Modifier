using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;

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

	private readonly ManualRandomSource randomSource = new ManualRandomSource();

	private ComboBox workflowInput;

	private Label randomDetails;

	private ComboBox gameSensitivityInput;

	private Label mouseSettingLabel;

	private Label gameEquivalentLabel;

	private Button resetButton;

	private int Workflow => state.PassThrough ? 0 : state.RandomManualMode ? 2 : 1;

	private int ActualDpi(int dpi) => GameDpiSettings.ActualDpi(dpi, state.GameDpiFactor);

	private bool UsesAutomaticTiming => !state.PassThrough && !state.RandomManualMode && state.Automatic;

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

	private Label driverLabel;

	private FlowLayoutPanel entriesPanel;

	private readonly ValueCard[] entries = new ValueCard[9];

	private NotifyIcon tray;

	private bool allowExit;
	private bool applying;
	private Task<bool> pendingApply;
	private bool closing;
	private bool exitReady;

	private int overrideDpi = -1;

	private int CurrentDpi
	{
		get
		{
			if (state.PassThrough) return state.BaseDpi;
			if (state.RandomManualMode) return state.RandomDpi;
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

	public MainForm() : this(null, null) { }

	internal MainForm(KernelMouseController controller, AppState suppliedState)
	{
		using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("Dpi.Icon"))
		{
			windowIcon = new Icon(stream, SystemInformation.IconSize);
		}
		trayIcon = new Icon(windowIcon, SystemInformation.SmallIconSize);
		base.Icon = windowIcon;
		state = suppliedState ?? AppState.Load();
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
		if (!GameDpiSettings.IsFactorValid(state.GameDpiFactor))
		{
			state.GameDpiFactor = 1;
			flag = true;
		}
		if (!ManualRandomSource.IsValid(state.RandomDpi, state.BaseDpi))
		{
			state.RandomDpi = state.BaseDpi;
			flag = true;
		}
		if (flag)
		{
			state.Save();
		}
		mouse = controller ?? new KernelMouseController();
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
		base.ClientSize = new Size(920, 590);
		MinimumSize = new Size(920, 590);
		base.StartPosition = FormStartPosition.CenterScreen;
		base.FormBorderStyle = FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Padding = new Padding(30, 20, 30, 20);
		tableLayoutPanel.RowCount = 7;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 106f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		base.Controls.Add(tableLayoutPanel);
		tableLayoutPanel.Controls.Add(BuildHeader(), 0, 0);
		tableLayoutPanel.Controls.Add(BuildValuePanel(), 0, 1);
		tableLayoutPanel.Controls.Add(BuildEntries(), 0, 2);
		tableLayoutPanel.Controls.Add(BuildSettings(), 0, 3);
		tableLayoutPanel.Controls.Add(BuildGameSettings(), 0, 4);
		tableLayoutPanel.Controls.Add(BuildTiming(), 0, 5);
		tableLayoutPanel.Controls.Add(BuildActions(), 0, 6);
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
		workflowInput = new ComboBox
		{
			Location = new Point(105, 8),
			Width = 300,
			DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = CardLight,
			ForeColor = TextMain,
			FlatStyle = FlatStyle.Flat
		};
		workflowInput.Items.AddRange(new object[] { "Pass-through · no DPI changes", "9-value sequence", "Random DPI · manual" });
		workflowInput.SelectedIndexChanged += async (s, e) =>
		{
			if (!updatingControls) await ChangeWorkflow(workflowInput.SelectedIndex);
		};
		panel2.Controls.Add(workflowInput);
		driverLabel = MakeLabel("DRIVER CHECKING", 280, TextMuted, new Point(570, 12));
		driverLabel.TextAlign = ContentAlignment.MiddleRight;
		panel2.Controls.Add(driverLabel);
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
		kindLabel = MakeLabel("BASE DPI", 200, Green, new Point(300, 54));
		gameEquivalentLabel = MakeLabel("", 300, TextMuted, new Point(525, 36));
		gameEquivalentLabel.Height = 50;
		kindLabel.Font = new Font("Segoe UI Semibold", 10f);
		dpiInput = MakeInput(new Point(24, 35), 205);
		dpiInput.AccessibleName = "Current output DPI";
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
		roundedPanel2.Controls.Add(gameEquivalentLabel);
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
		randomDetails = MakeLabel("", 830, TextMain, new Point(0, 27));
		randomDetails.Height = 65;
		panel2.Controls.Add(randomDetails);
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
		panel2.Controls.Add(MakeLabel("BASE SENSITIVITY DPI", 220, TextMuted, new Point(0, 0)));
		panel2.Controls.Add(MakeLabel("1600 recommended", 180, TextMuted, new Point(675, 25)));
		panel2.Controls.Add(MakeLabel("LOWER RANGE DPI", 200, TextMuted, new Point(225, 0)));
		panel2.Controls.Add(MakeLabel("UPPER RANGE DPI", 200, TextMuted, new Point(450, 0)));
		baseInput = MakeInput(new Point(0, 25), 190);
		baseInput.AccessibleName = "Base sensitivity DPI";
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

	private Control BuildGameSettings()
	{
		Panel panel = new Panel { Dock = DockStyle.Fill };
		panel.Controls.Add(MakeLabel("MY IN-GAME SENSITIVITY", 250, TextMuted, new Point(0, 0)));
		gameSensitivityInput = new ComboBox
		{
			Location = new Point(0, 25), Width = 350, DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = CardLight, ForeColor = TextMain, FlatStyle = FlatStyle.Flat,
			AccessibleName = "My in-game sensitivity"
		};
		gameSensitivityInput.Items.AddRange(new object[] { "Normal · unchanged", "I've halved it · ×0.5", "I've quartered it · ×0.25" });
		gameSensitivityInput.SelectedIndexChanged += async (s, e) =>
		{
			if (!updatingControls) await SetGameDpiFactor(1 << gameSensitivityInput.SelectedIndex);
		};
		panel.Controls.Add(gameSensitivityInput);
		mouseSettingLabel = MakeLabel("", 475, TextMain, new Point(380, 22));
		mouseSettingLabel.Height = 50;
		panel.Controls.Add(mouseSettingLabel);
		return panel;
	}

	private async Task<bool> SetGameDpiFactor(int factor)
	{
		if (applying || closing || !GameDpiSettings.IsFactorValid(factor) || factor == state.GameDpiFactor)
		{
			UpdateControls();
			return false;
		}
		int previous = state.GameDpiFactor;
		if (!state.PassThrough && !await ApplyTarget(CurrentDpi, state.BaseDpi, factor))
		{
			UpdateControls();
			return false;
		}
		state.GameDpiFactor = factor;
		if (!state.Save())
		{
			state.GameDpiFactor = previous;
			if (!state.PassThrough) await ApplyTarget(CurrentDpi, state.BaseDpi, previous);
			UpdateControls();
			return false;
		}
		StopTiming();
		UpdateView();
		UpdateControls();
		return true;
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
		textBox.MaxLength = 6;
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
		resetButton = button;
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
		if (message.Msg == 0x8040)
        {
            allowExit = true;
            Close();
            return;
        }
        if (message.Msg == 786 && !applying && !closing)
		{
			int num = message.WParam.ToInt32();
			if (num == 1 && !UsesAutomaticTiming && state.EnablePlus)
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
					if (UsesAutomaticTiming)
					{
						ToggleTiming();
					}
					break;
				}
			}
		}
		base.WndProc(ref message);
	}

	private async Task<bool> AdvanceIndex()
	{
		if (state.PassThrough || state.RandomManualMode) return false;
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
		if (!await ApplyTarget(array[num], state.BaseDpi))
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
			await ApplyTarget(currentDpi, state.BaseDpi);
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

	private async void RequestNext()
	{
        if (applying || closing || state.PassThrough) return;
		if (state.RandomManualMode)
		{
			await RandomizeDpi();
		}
		else if (!UsesAutomaticTiming)
		{
			await AdvanceIndex();
		}
	}

	private async Task<bool> RandomizeDpi()
	{
		if (state.PassThrough || !state.RandomManualMode || applying || closing) return false;
		return await SetRandomDpi(randomSource.Next(state.RandomDpi, state.BaseDpi));
	}

	private async Task<bool> SetRandomDpi(int target)
	{
		if (state.PassThrough || !state.RandomManualMode || !ManualRandomSource.IsValid(target, state.BaseDpi)) return false;
		int previous = state.RandomDpi;
		if (!await ApplyTarget(target, state.BaseDpi)) return false;
		state.RandomDpi = target;
		if (!state.Save())
		{
			state.RandomDpi = previous;
			await ApplyTarget(previous, state.BaseDpi);
			return false;
		}
		StopTiming();
		UpdateView();
		return true;
	}

	private async Task<bool> ChangeWorkflow(int workflow)
	{
		if (applying || closing || workflow < 0 || workflow > 2 || workflow == Workflow)
		{
			UpdateControls();
			return false;
		}
		int previousDpi = CurrentDpi;
		bool previousPassThrough = state.PassThrough;
		StopTiming();
		bool randomMode = workflow == 2;
		int target = randomMode ? state.BaseDpi : (overrideDpi >= 0 ? overrideDpi : state.Index >= 0 ? state.Values[state.Index] : state.BaseDpi);
		if (!(workflow == 0 ? await RestoreMouse() : await ApplyTarget(target, state.BaseDpi)))
		{
			UpdateControls();
			return false;
		}
		bool previousMode = state.RandomManualMode;
		int previousRandom = state.RandomDpi;
		state.RandomManualMode = randomMode;
		state.PassThrough = workflow == 0;
		if (randomMode) state.RandomDpi = state.BaseDpi;
		if (!state.Save())
		{
			state.RandomManualMode = previousMode;
			state.PassThrough = previousPassThrough;
			state.RandomDpi = previousRandom;
			if (previousPassThrough) await RestoreMouse();
			else await ApplyTarget(previousDpi, state.BaseDpi);
			UpdateControls();
			return false;
		}
		UpdateSettingsFields();
		UpdateView();
		UpdateControls();
		UpdateHotkeys();
		return true;
	}

	private async void ResetValues()
	{
        if (applying || closing || state.PassThrough) return;
		if (state.RandomManualMode)
		{
			await SetRandomDpi(state.BaseDpi);
			return;
		}
		int[] values = state.Values;
		int index = state.Index;
		int currentDpi = CurrentDpi;
		int[] values2 = seriesSource.Create(state.BaseDpi, state.LowerRangeDpi, state.UpperRangeDpi);
		if (await ApplyTarget(state.BaseDpi, state.BaseDpi))
		{
			state.Values = values2;
			state.Index = -1;
			if (!state.Save())
			{
				state.Values = values;
				state.Index = index;
				await ApplyTarget(currentDpi, state.BaseDpi);
			}
			else
			{
				overrideDpi = -1;
				StopTiming();
				UpdateView();
			}
		}
	}

	private async void ApplyCurrent()
	{
        if (applying || closing) return;
		if (state.PassThrough) await RestoreMouse();
		else await ApplyTarget(CurrentDpi, state.BaseDpi);
	}

	private async Task<bool> RestoreMouse()
	{
		if (applying || closing) return false;
		applying = true;
		UpdateControls();
		try
		{
			pendingApply = Task.Run(() => mouse.Restore());
			bool restored = await pendingApply;
			driverLabel.Text = restored ? "PASS-THROUGH · APP OFF" : "RESTORE FAILED";
			driverLabel.ForeColor = restored ? Green : Accent;
			return restored;
		}
		finally { applying = false; UpdateControls(); }
	}


    private async Task<bool> ApplyTarget(int target, int baseline, int? gameFactor = null)
    {
        if (applying || closing) return false;
        applying = true;
		UpdateControls();
        driverLabel.Text = "APPLYING DPI...";
        driverLabel.ForeColor = TextMuted;
        try
        {
            int factor = gameFactor ?? state.GameDpiFactor;
            pendingApply = Task.Run(() => mouse.Apply(GameDpiSettings.ActualDpi(target, factor), GameDpiSettings.ActualDpi(baseline, factor)));
            bool applied = await pendingApply;
            driverLabel.Text = applied ? "DRIVER READY" : (mouse.InputRejected ? "INVALID DPI" : "DRIVER UNAVAILABLE");
            driverLabel.ForeColor = applied ? Green : Accent;
            return applied;
        }
        finally
        {
            applying = false;
			UpdateControls();
            if (clock.IsRunning && UsesAutomaticTiming && !closing) wakeTimer.Start();
        }
    }

	private async Task<bool> ApplyOverride(int target)
	{
		if (state.PassThrough || state.RandomManualMode) return false;
		if (!await ApplyTarget(target, state.BaseDpi))
		{
			return false;
		}
		StopTiming();
		overrideDpi = target;
		UpdateView();
		return true;
	}

	private async void CommitValue()
	{
        if (applying || closing) return;
		if (state.PassThrough || state.RandomManualMode) { UpdateView(); return; }
		if (!int.TryParse(dpiInput.Text, out var result) || result % state.GameDpiFactor != 0 || result == ActualDpi(CurrentDpi) || !await ApplyOverride(result / state.GameDpiFactor))
		{
			dpiInput.Text = ActualDpi(CurrentDpi).ToString();
		}
	}

	private void UpdateSettingsFields()
	{
		baseInput.Text = state.BaseDpi.ToString();
		lowerInput.Text = (state.RandomManualMode ? state.BaseDpi - ManualRandomSource.Minimum(state.BaseDpi) : state.LowerRangeDpi).ToString();
		upperInput.Text = (state.RandomManualMode ? 800 : state.UpperRangeDpi).ToString();
	}

	private async void CommitSettings()
	{
        if (applying || closing) return;
		if (!int.TryParse(baseInput.Text, out var result) || result < 1 || result > 9999 || !int.TryParse(lowerInput.Text, out var result2) || result2 < 0 || !int.TryParse(upperInput.Text, out var result3) || result3 < 0)
		{
			UpdateSettingsFields();
			return;
		}
		if (state.RandomManualMode)
		{
			result2 = state.LowerRangeDpi;
			result3 = state.UpperRangeDpi;
		}
		result2 = Math.Min(result2, result - 1);
		result3 = Math.Min(result3, result);
		await ChangeOutputSettings(result, result2, result3);
	}

	private async Task<bool> ChangeOutputSettings(int result, int result2, int result3)
	{
		if (applying || closing) return false;
		if (!SeriesSource.IsConfigurationValid(result, result2, result3) || (result == state.BaseDpi && result2 == state.LowerRangeDpi && result3 == state.UpperRangeDpi))
		{
			UpdateSettingsFields();
			return false;
		}
		int baseDpi = state.BaseDpi;
		int lowerRangeDpi = state.LowerRangeDpi;
		int upperRangeDpi = state.UpperRangeDpi;
		int currentDpi = CurrentDpi;
		int[] values = state.Values;
		int index = state.Index;
		int randomDpi = state.RandomDpi;
		int[] values2 = seriesSource.Create(result, result2, result3);
		if (!state.PassThrough && !await ApplyTarget(result, result))
		{
			UpdateSettingsFields();
			return false;
		}
		state.BaseDpi = result;
		state.LowerRangeDpi = result2;
		state.UpperRangeDpi = result3;
		state.Values = values2;
		state.Index = -1;
		state.RandomDpi = result;
		bool saved = state.Save();
		if (!saved)
		{
			state.BaseDpi = baseDpi;
			state.LowerRangeDpi = lowerRangeDpi;
			state.UpperRangeDpi = upperRangeDpi;
			state.Values = values;
			state.Index = index;
			state.RandomDpi = randomDpi;
			if (!state.PassThrough) await ApplyTarget(currentDpi, baseDpi);
		}
		else
		{
			overrideDpi = -1;
			StopTiming();
		}
		UpdateSettingsFields();
		UpdateView();
		return saved;
	}

	private void UpdateView()
	{
		int currentDpi = CurrentDpi;
		bool flag = currentDpi == state.BaseDpi;
		dpiInput.Text = ActualDpi(currentDpi).ToString();
		kindLabel.Text = ((overrideDpi >= 0) ? "OVERRIDE DPI" : ((state.Index < 0) ? "BASE DPI" : (flag ? "BASE DPI" : "DPI")));
		kindLabel.ForeColor = (flag ? Green : Accent);
		indexLabel.Text = ((overrideDpi >= 0) ? "OVERRIDE" : ((state.Index < 0) ? "READY" : ("ENTRY " + (state.Index + 1) + " OF 9")));
		if (state.RandomManualMode)
		{
			indexLabel.Text = "RANDOM DPI · MANUAL";
			kindLabel.Text = flag ? "BASE DPI" : "RANDOM DPI";
		}
		if (state.PassThrough)
		{
			indexLabel.Text = "PASS-THROUGH";
			kindLabel.Text = "MOUSE DPI SETTING";
		}
		entriesPanel.Visible = Workflow == 1;
		randomDetails.Visible = Workflow != 1;
		randomDetails.Text = state.PassThrough ? "The app does not modify movement, run a timer, or use its change hotkeys.\nYour mouse and in-game settings control sensitivity." : $"App output: {ActualDpi(ManualRandomSource.Minimum(state.BaseDpi))}–{ActualDpi(ManualRandomSource.Maximum(state.BaseDpi))} DPI · Mouse: {ActualDpi(state.BaseDpi)} DPI\nIn-game equivalent: {ManualRandomSource.Minimum(state.BaseDpi)}–{ManualRandomSource.Maximum(state.BaseDpi)} · Click CHANGE DPI to randomize.";
		mouseSettingLabel.Text = $"Set your mouse in G HUB to {ActualDpi(state.BaseDpi)} DPI.\n{state.BaseDpi} base × {state.GameDpiFactor} · App does not change G HUB.";
		gameEquivalentLabel.Text = $"In-game equivalent: {currentDpi} DPI\nGame sensitivity: ×{1.0 / state.GameDpiFactor:0.##}";
		for (int i = 0; i < entries.Length; i++)
		{
			ValueCard valueCard = entries[i];
			valueCard.Dpi = ActualDpi(state.Values[i]);
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
        if (applying || closing) return;
		if (state.RandomManualMode) { UpdateTimingFields(); return; }
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
        if (applying || closing) return;
		if (updatingControls)
		{
			return;
		}
		bool automatic = state.Automatic;
		bool enablePlus = state.EnablePlus;
		bool flag = state.RandomManualMode ? state.Automatic : !modeToggle.Checked;
		bool flag2 = UsesAutomaticTiming ? state.EnablePlus : plusToggle.Checked;
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
		workflowInput.SelectedIndex = Workflow;
		workflowInput.Enabled = !applying && !closing;
		modeToggle.Checked = !UsesAutomaticTiming;
		modeToggle.Text = UsesAutomaticTiming ? "AUTO" : "MANUAL";
		modeToggle.Enabled = Workflow == 1 && !applying && !closing;
		plusToggle.Enabled = !state.PassThrough && !UsesAutomaticTiming && !applying && !closing;
		plusToggle.Checked = !state.PassThrough && !UsesAutomaticTiming && state.EnablePlus;
		nextButton.Enabled = !state.PassThrough && !UsesAutomaticTiming && !applying && !closing;
		resetButton.Enabled = !state.PassThrough && !applying && !closing;
		nextButton.Text = (state.RandomManualMode ? "CHANGE DPI" : "NEXT") + (state.EnablePlus ? "     NUM +" : "");
		nextButton.BackColor = (nextButton.Enabled ? Accent : CardLight);
		timingButton.Enabled = UsesAutomaticTiming && !applying && !closing;
		timingButton.Text = state.PassThrough ? "PASS-THROUGH" : state.RandomManualMode ? "MANUAL ONLY" : (clock.IsRunning ? "PAUSE" : (clock.IsSet ? "RESUME" : "START")) + "     NUM *";
		dpiInput.ReadOnly = state.PassThrough || state.RandomManualMode;
		baseInput.Enabled = !applying && !closing;
		gameSensitivityInput.Enabled = !applying && !closing;
		gameSensitivityInput.SelectedIndex = state.GameDpiFactor == 4 ? 2 : state.GameDpiFactor == 2 ? 1 : 0;
		lowerInput.ReadOnly = state.RandomManualMode;
		upperInput.ReadOnly = state.RandomManualMode;
		durationInput.Enabled = !state.RandomManualMode;
		countdownInput.Enabled = !state.RandomManualMode;
		updatingControls = false;
	}

	private void UpdateHotkeys()
	{
		if (base.IsHandleCreated)
		{
			UnregisterHotKey(base.Handle, 1);
			UnregisterHotKey(base.Handle, 3);
			UnregisterHotKey(base.Handle, 2);
			if (!state.PassThrough) RegisterHotKey(base.Handle, 2, 16384u, 111);
			if (!state.PassThrough && !UsesAutomaticTiming && state.EnablePlus)
			{
				RegisterHotKey(base.Handle, 1, 16384u, 107);
			}
			if (UsesAutomaticTiming)
			{
				RegisterHotKey(base.Handle, 3, 16384u, 106);
			}
		}
	}

	private void ToggleTiming()
	{
        if (applying || closing) return;
		if (!UsesAutomaticTiming)
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

	private async void WakeTiming()
	{
		wakeTimer.Stop();
        if (applying || closing) return;
		if (!clock.IsRunning || !UsesAutomaticTiming)
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
			if (!await AdvanceIndex())
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
        else if (!exitReady)
        {
            e.Cancel = true;
            if (!closing) FinishExit();
        }
        else base.OnFormClosing(e);
    }
    private async void FinishExit()
    {
        closing = true;
        StopTiming();
        if (pendingApply != null) await pendingApply;
        bool restored = await Task.Run(() => mouse.Restore());
        if (!restored)
        {
            closing = false;
            allowExit = false;
            driverLabel.Text = "RESTORE FAILED";
            MessageBox.Show("The previous mouse settings could not be restored. Retry Exit from the tray menu.", Text);
            return;
        }
        exitReady = true;
        allowExit = true;
        Close();
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
