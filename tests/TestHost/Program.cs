using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Program
{
    static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly List<object> Results = new List<object>();
    static string ArtifactRoot;
    static Assembly AppAssembly;
    static Form Form;
    static byte[] PreviousState;
    static string StatePath;
    static readonly List<Process> Launched = new List<Process>();

    [STAThread]
    static int Main(string[] args)
    {
        ArtifactRoot = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(ArtifactRoot);
        var installedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DPI_Modifier");
        AppAssembly = Assembly.LoadFile(Path.Combine(installedRoot, "DPI.exe"));
        var stateType = AppAssembly.GetType("Dpi.AppState");
        StatePath = (string)stateType.GetProperty("StatePath", All).GetValue(null, null);
        PreviousState = File.Exists(StatePath) ? File.ReadAllBytes(StatePath) : null;
        var backupRoot = Path.GetFullPath(Path.Combine(ArtifactRoot, "..", "..", ".private"));
        Directory.CreateDirectory(backupRoot);
        if (PreviousState != null) File.WriteAllBytes(Path.Combine(backupRoot, "state-before-tests.json"), PreviousState);
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SetProcessDPIAware();
        try
        {
            if (Process.GetProcessesByName("DPI").Length != 0) throw new InvalidOperationException("DPI_PROCESS_PRESENT");
            Run("dependencies_runtime", () => {
                foreach (var name in AppAssembly.GetReferencedAssemblies())
                {
                    var dependency = Assembly.Load(name);
                    Assert(dependency.GlobalAssemblyCache, "NON_OS_ASSEMBLY");
                }
                return new { count = AppAssembly.GetReferencedAssemblies().Length, development_runtime_required = false, clr = Environment.Version.ToString() };
            });
            Run("series_invariants", SeriesInvariants);
            Run("deadline_clock", DeadlineClock);
            Run("driver_ioctl", DriverIoctl);
            if (File.Exists(StatePath)) File.Delete(StatePath);
            Run("first_launch_executable", () => {
                var process = Launch(Path.Combine(installedRoot, "DPI.exe"), "");
                PumpUntil(() => process.HasExited || VisibleWindow(process) != IntPtr.Zero, 8000);
                Assert(!process.HasExited, "FIRST_LAUNCH_EXITED");
                Assert(VisibleWindow(process) != IntPtr.Zero, "FIRST_LAUNCH_NO_WINDOW");
                Assert(process.Responding, "FIRST_LAUNCH_NOT_RESPONDING");
                var state = ReadState();
                Assert(Convert.ToInt32(state["BaseDpi"]) == 1600, "BASE_DEFAULT");
                return new { alive = true, responding = true, state_created = File.Exists(StatePath) };
            });
            Run("first_launch_capture", () => { Capture(VisibleWindow(Launched[0]), "first-launch.png"); return new { image = "first-launch.png" }; });
            Run("single_instance_executable", () => {
                var duplicate = Launch(Path.Combine(installedRoot, "DPI.exe"), "");
                Assert(duplicate.WaitForExit(8000), "DUPLICATE_NOT_EXITED");
                Assert(Process.GetProcessesByName("DPI").Length == 1, "DUPLICATE_RUNNING");
                return new { processes = 1, duplicate_exit_code = duplicate.ExitCode };
            });
            StopLaunched();
            Run("startup_hidden_executable", () => {
                var launcher = Launch(Path.Combine(installedRoot, "Launch.exe"), "--hidden");
                Assert(launcher.WaitForExit(8000), "LAUNCHER_NOT_EXITED");
                PumpUntil(() => Process.GetProcessesByName("DPI").Length > 0, 8000);
                var app = Process.GetProcessesByName("DPI").Single();
                Launched.Add(app);
                Pump(500);
                Assert(VisibleWindow(app) == IntPtr.Zero, "HIDDEN_WINDOW_VISIBLE");
                return new { processes = 1, visible_window = false, launcher_exit_code = launcher.ExitCode };
            });
            StopLaunched();
            Run("corrupt_state_executable", () => {
                File.WriteAllText(StatePath, "{corrupt");
                var process = Launch(Path.Combine(installedRoot, "DPI.exe"), "");
                PumpUntil(() => process.HasExited || VisibleWindow(process) != IntPtr.Zero, 8000);
                Assert(!process.HasExited && VisibleWindow(process) != IntPtr.Zero, "CORRUPT_STATE_CRASH");
                Assert(Convert.ToInt32(ReadState()["BaseDpi"]) == 1600, "CORRUPT_STATE_DEFAULTS");
                return new { recovered = true };
            });
            StopLaunched();
            if (File.Exists(StatePath)) File.Delete(StatePath);
            Form = (Form)Activator.CreateInstance(AppAssembly.GetType("Dpi.MainForm"), true);
            Form.Show();
            Pump(150);
            Run("manual_rotation", ManualRotation);
            Run("override_validation", OverrideValidation);
            Run("settings_validation", SettingsValidation);
            Run("automatic_pause_resume", AutomaticPauseResume);
            Run("automatic_countdown", AutomaticCountdown);
            Run("persistence_rollback", PersistenceRollback);
            Run("tray_restore", TrayRestore);
            Capture(Form.Handle, "workflow.png");
            Field(Form, "allowExit", true);
            Form.Close();
            Form.Dispose();
            Form = null;
        }
        catch (Exception error)
        {
            Results.Add(new { name = "test_host", status = "fail", code = ErrorCode(error), exception = Root(error).GetType().FullName });
        }
        finally
        {
            if (Form != null) Form.Dispose();
            StopLaunched();
            var controller = Activator.CreateInstance(AppAssembly.GetType("Dpi.KernelMouseController"), true);
            try { Call(controller, "Apply", 1600, 1600); }
            finally { ((IDisposable)controller).Dispose(); }
            if (PreviousState == null)
            {
                if (File.Exists(StatePath)) File.Delete(StatePath);
            }
            else File.WriteAllBytes(StatePath, PreviousState);
            if (File.Exists(StatePath + ".tmp")) File.Delete(StatePath + ".tmp");
            File.WriteAllText(Path.Combine(ArtifactRoot, "runtime-tests.json"), new JavaScriptSerializer().Serialize(Results));
        }
        return Results.Any(r => (string)r.GetType().GetProperty("status").GetValue(r, null) != "pass") ? 1 : 0;
    }

    static void Run(string name, Func<object> test)
    {
        var stopwatch = Stopwatch.StartNew();
        try { var data = test(); Results.Add(new { name, status = "pass", elapsed_ms = stopwatch.ElapsedMilliseconds, data }); }
        catch (Exception error) { Results.Add(new { name, status = "fail", elapsed_ms = stopwatch.ElapsedMilliseconds, code = ErrorCode(error), exception = Root(error).GetType().FullName }); }
        File.WriteAllText(Path.Combine(ArtifactRoot, "runtime-tests.json"), new JavaScriptSerializer().Serialize(Results));
    }

    static object SeriesInvariants()
    {
        var type = AppAssembly.GetType("Dpi.SeriesSource");
        var source = Activator.CreateInstance(type, true);
        var configs = new[] { new[] { 1600, 550, 550 }, new[] { 1, 1, 0 }, new[] { 9999, 0, 9999 }, new[] { 800, 800, 800 } };
        int samples = 0;
        foreach (var config in configs)
        {
            for (int trial = 0; trial < 1000; trial++)
            {
                var series = (int[])Call(source, "Create", config[0], config[1], config[2]);
                Assert(series.Length == 9 && series.Count(v => v == config[0]) == 3, "SERIES_COMPOSITION");
                Assert(series.All(v => v >= config[0] - config[1] && v <= config[0] + config[2]), "SERIES_BOUND");
                samples++;
            }
        }
        var invalid = new[] { new[] { 0, 1, 1 }, new[] { 10000, 1, 1 }, new[] { 1600, -1, 1 }, new[] { 1600, 0, 0 } };
        foreach (var config in invalid)
            Assert(!(bool)type.GetMethod("IsConfigurationValid", All).Invoke(null, config.Select(v => (object)v).ToArray()), "INVALID_CONFIGURATION_ACCEPTED");
        return new { samples, configurations = configs.Length, invalid_configurations = invalid.Length };
    }

    static object DeadlineClock()
    {
        var clock = Activator.CreateInstance(AppAssembly.GetType("Dpi.DeadlineClock"), true);
        Call(clock, "Start", 1000, 100L, true);
        Assert((int)Call(clock, "Remaining", 350L) == 750, "CLOCK_REMAINING");
        Call(clock, "Pause", 350L);
        Assert((int)Call(clock, "Remaining", 9000L) == 750, "CLOCK_PAUSE");
        Call(clock, "Resume", 10000L);
        Assert((int)Call(clock, "Remaining", 10250L) == 500, "CLOCK_RESUME");
        Assert((int)Call(clock, "Remaining", 11000L) == 0, "CLOCK_DEADLINE");
        Call(clock, "Clear");
        Assert(!(bool)Property(clock, "IsSet"), "CLOCK_CLEAR");
        return new { assertions = 5 };
    }

    static object DriverIoctl()
    {
        var controller = Activator.CreateInstance(AppAssembly.GetType("Dpi.KernelMouseController"), true);
        int accepted = 0;
        try
        {
            foreach (var target in new[] { 1600, 1050, 2150, 1600 })
            {
                Assert((bool)Call(controller, "Apply", target, 1600), "DRIVER_IOCTL_REJECTED");
                accepted++;
            }
            foreach (var pair in new[] { new[] { -1, 1600 }, new[] { 3201, 1600 }, new[] { 1600, 0 }, new[] { 1600, 10000 } })
                Assert(!(bool)Call(controller, "Apply", pair[0], pair[1]), "DRIVER_BOUND_ACCEPTED");
            return new { accepted, invalid_rejected = 4, restored_target = 1600, hardware_motion_observed = false };
        }
        finally { Call(controller, "Apply", 1600, 1600); ((IDisposable)controller).Dispose(); }
    }

    static object ManualRotation()
    {
        ((CheckBox)Field(Form, "modeToggle")).Checked = true;
        ((CheckBox)Field(Form, "plusToggle")).Checked = true;
        Call(Form, "ResetValues");
        for (int i = 0; i < 18; i++)
        {
            ((Button)Field(Form, "nextButton")).PerformClick();
            Pump(20);
            Assert((int)Field(Field(Form, "state"), "Index") == i % 9, "MANUAL_ADVANCE");
            Assert(Convert.ToInt32(ReadState()["Index"]) == i % 9, "MANUAL_PERSISTENCE");
        }
        Call(Form, "ResetValues");
        Assert((int)Field(Field(Form, "state"), "Index") == -1, "RESET_INDEX");
        return new { transitions = 18, resets = 2 };
    }

    static object OverrideValidation()
    {
        var input = (TextBox)Field(Form, "dpiInput");
        input.Text = "2150";
        Call(Form, "CommitValue");
        Assert((int)Property(Form, "CurrentDpi") == 2150, "OVERRIDE_NOT_APPLIED");
        foreach (var invalid in new[] { "abc", "-1", "3201", "2147483648" })
        {
            input.Text = invalid;
            Call(Form, "CommitValue");
            Assert(input.Text == "2150" && (int)Property(Form, "CurrentDpi") == 2150, "OVERRIDE_INVALID_CHANGED_STATE");
        }
        Call(Form, "ResetValues");
        return new { accepted = 1, rejected = 4 };
    }

    static object SettingsValidation()
    {
        var baseline = (TextBox)Field(Form, "baseInput");
        baseline.Text = "0";
        Call(Form, "CommitSettings");
        Assert(baseline.Text == "1600", "SETTINGS_INVALID_BASE");
        baseline.Text = "800";
        ((TextBox)Field(Form, "lowerInput")).Text = "200";
        ((TextBox)Field(Form, "upperInput")).Text = "200";
        Call(Form, "CommitSettings");
        Assert((int)Field(Field(Form, "state"), "BaseDpi") == 800, "SETTINGS_VALID_BASE");
        baseline.Text = "1600";
        ((TextBox)Field(Form, "lowerInput")).Text = "550";
        ((TextBox)Field(Form, "upperInput")).Text = "550";
        Call(Form, "CommitSettings");
        return new { accepted = 2, rejected = 1 };
    }

    static object AutomaticPauseResume()
    {
        ((CheckBox)Field(Form, "modeToggle")).Checked = false;
        ((TextBox)Field(Form, "durationInput")).Text = "1";
        ((TextBox)Field(Form, "countdownInput")).Text = "0";
        Call(Form, "CommitTiming");
        var button = (Button)Field(Form, "timingButton");
        button.PerformClick();
        Pump(1150);
        int index = (int)Field(Field(Form, "state"), "Index");
        Assert(index >= 1, "AUTOMATIC_NO_ADVANCE");
        button.PerformClick();
        int pausedIndex = (int)Field(Field(Form, "state"), "Index");
        Pump(1200);
        Assert((int)Field(Field(Form, "state"), "Index") == pausedIndex, "PAUSE_ADVANCED");
        button.PerformClick();
        Pump(1150);
        Assert((int)Field(Field(Form, "state"), "Index") != pausedIndex, "RESUME_NO_ADVANCE");
        Call(Form, "StopTiming");
        ((TextBox)Field(Form, "durationInput")).Text = "105";
        ((TextBox)Field(Form, "countdownInput")).Text = "3";
        Call(Form, "CommitTiming");
        Call(Form, "ResetValues");
        return new { transitions_observed = true, pause_verified = true, resume_verified = true, countdown_tested = false };
    }

    static object PersistenceRollback()
    {
        ((CheckBox)Field(Form, "modeToggle")).Checked = true;
        var state = Field(Form, "state");
        int before = (int)Field(state, "Index");
        using (var stream = new FileStream(StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = (bool)Call(Form, "AdvanceIndex");
            Assert(!result && (int)Field(state, "Index") == before, "PERSISTENCE_ROLLBACK");
        }
        Assert(!File.Exists(StatePath + ".tmp"), "PERSISTENCE_TEMP_REMAINING");
        return new { accepted = false, index_unchanged = true, temporary_file_remaining = false };
    }

    static object AutomaticCountdown()
    {
        ((CheckBox)Field(Form, "modeToggle")).Checked = false;
        ((TextBox)Field(Form, "durationInput")).Text = "1";
        ((TextBox)Field(Form, "countdownInput")).Text = "1";
        Call(Form, "CommitTiming");
        Call(Form, "ResetValues");
        Form.Activate();
        ((Button)Field(Form, "timingButton")).PerformClick();
        Pump(100);
        var overlay = Field(Form, "countdown");
        Assert(overlay != null && (bool)Property(overlay, "Visible"), "COUNTDOWN_NOT_VISIBLE");
        Pump(1100);
        Assert((int)Field(Field(Form, "state"), "Index") >= 0, "COUNTDOWN_NO_ADVANCE");
        ((Button)Field(Form, "timingButton")).PerformClick();
        Pump(50);
        Assert(!(bool)Property(overlay, "Visible"), "COUNTDOWN_PAUSE_VISIBLE");
        Call(Form, "StopTiming");
        ((TextBox)Field(Form, "durationInput")).Text = "105";
        ((TextBox)Field(Form, "countdownInput")).Text = "3";
        Call(Form, "CommitTiming");
        Call(Form, "ResetValues");
        return new { overlay_visible = true, transition_observed = true, overlay_hidden_on_pause = true };
    }

    static object TrayRestore()
    {
        Form.Close();
        Pump(100);
        Assert(!Form.Visible && !Form.IsDisposed, "TRAY_CLOSE");
        Call(Form, "RestoreWindow");
        Pump(100);
        Assert(Form.Visible, "TRAY_RESTORE");
        return new { hidden_on_close = true, restored = true };
    }

    static Process Launch(string path, string arguments)
    {
        var info = new ProcessStartInfo(path, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) };
        var process = Process.Start(info);
        Launched.Add(process);
        return process;
    }

    static void StopLaunched()
    {
        foreach (var process in Launched)
        {
            try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
            catch (InvalidOperationException) {}
        }
        Launched.Clear();
    }

    static IntPtr VisibleWindow(Process process)
    {
        process.Refresh();
        return process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle) ? process.MainWindowHandle : IntPtr.Zero;
    }

    static void Capture(IntPtr handle, string fileName)
    {
        Rect rect;
        Assert(GetWindowRect(handle, out rect), "SCREENSHOT_RECT");
        using (var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            bool captured = PrintWindow(handle, dc, 2);
            graphics.ReleaseHdc(dc);
            if (!captured)
            {
                SetForegroundWindow(handle);
                Pump(200);
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
            }
            bitmap.Save(Path.Combine(ArtifactRoot, fileName), System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    static Dictionary<string, object> ReadState() => new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
    static object Field(object target, string name) => target.GetType().GetField(name, All).GetValue(target);
    static void Field(object target, string name, object value) => target.GetType().GetField(name, All).SetValue(target, value);
    static object Property(object target, string name) => target.GetType().GetProperty(name, All).GetValue(target, null);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, All).Invoke(target, args);
    static Exception Root(Exception error) => error is TargetInvocationException && error.InnerException != null ? Root(error.InnerException) : error;
    static string ErrorCode(Exception error) => Root(error).Message.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '_') ? Root(error).Message : "EXCEPTION";
    static void Assert(bool value, string code) { if (!value) throw new InvalidOperationException(code); }
    static void Pump(int milliseconds) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
    static void PumpUntil(Func<bool> condition, int milliseconds) { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < milliseconds) Pump(20); }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
}
