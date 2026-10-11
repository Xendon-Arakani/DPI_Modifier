using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Dpi;
using Dpi.Setup;

internal static class SafetyTests
{
    private static readonly List<object> results = new List<object>();
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        results.Add(new { name, status = "pass" });
    }
    private sealed class FakeBackend : IMouseBackend
    {
        public readonly object Snapshot = new object();
        public int Captures, Writes, Restores;
        public int LastTarget, LastBaseline;
        public bool FailCapture, FailWrite, FailRestore;
        public ManualResetEventSlim Delay;
        public object Capture() { Captures++; if (FailCapture) throw new IOException("missing driver"); return Snapshot; }
        public void Apply(int target, int baseline) { Writes++; LastTarget = target; LastBaseline = baseline; Delay?.Wait(); if (FailWrite) throw new IOException("write failed"); }
        public void Restore(object snapshot) { if (!ReferenceEquals(snapshot, Snapshot)) throw new Exception("wrong snapshot"); Restores++; if (FailRestore) throw new IOException("restore failed"); }
    }
    private sealed class FakeRegistration : IMouseRegistration
    {
        public string[] Filters = { "VendorFilter", "DpiFilter", "mouclass" };
        public bool FailDelete, FailDetach;
        public bool Deleted;
        public string[] ReadFilters() => Filters;
        public void WriteFilters(string[] filters) { if (FailDetach) throw new IOException("registry unavailable"); Filters = filters; }
        public void DeleteService(string name)
        {
            if (Filters.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase))) throw new Exception("service deleted before detach");
            if (FailDelete) throw new IOException("service unavailable");
            Deleted = true;
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        try
        {
            foreach (int baseline in new[] { 400, 800, 1600, 9999 })
            foreach (int target in new[] { 1, baseline / 2, baseline, baseline * 2 })
            {
                var config = RawAccelBackend.CreateConfig(target, baseline);
                Check(config.Errors() == null, $"profile_valid_{baseline}_{target}");
                using var math = new ManagedAccel(config.profiles[0]);
                var actual = math.Accelerate(100, -80, 1000.0 / baseline, 1);
                Check(Math.Abs(actual.Item1 - 100.0 * target / baseline) < 1e-8 &&
                    Math.Abs(actual.Item2 + 80.0 * target / baseline) < 1e-8, $"raw_input_ratio_{baseline}_{target}");
            }
            var fake = new FakeBackend();
            using (var controller = new KernelMouseController(fake))
            {
                Check(!controller.Apply(0, 1600) && controller.InputRejected && fake.Captures == 0, "zero_dpi_cannot_disable_mouse");
                Check(!controller.Apply(GameDpiSettings.MaximumOutputDpi + 1, 1600) && !controller.Apply(1, 0) && !controller.Apply(1600, 65536), "out_of_range_rejected_before_io");
                Check(controller.Apply(800, 1600) && controller.Apply(2400, 1600) && fake.Captures == 1, "capture_original_once");
                Check(controller.Apply(1600, 3200) && fake.LastTarget == 1600 && fake.LastBaseline == 3200, "hardware_3200_output_1600_supported");
                Check(controller.Apply(1600, 48000) && controller.Apply(3200, 400), "hardware_dpi_independent_of_output_supports_downscale_and_upscale");
                Check(controller.Restore() && fake.Restores == 1, "restore_exact_original_configuration");
                controller.Restore();
                Check(fake.Restores == 1, "restore_is_idempotent");
            }
            fake = new FakeBackend { FailCapture = true };
            using (var controller = new KernelMouseController(fake))
                Check(!controller.Apply(1600, 1600) && fake.Writes == 0, "no_write_without_snapshot");
            fake = new FakeBackend { FailWrite = true };
            using (var controller = new KernelMouseController(fake))
            {
                Check(!controller.Apply(800, 1600), "write_failure_reported");
                Check(controller.Restore() && fake.Restores == 1, "failed_write_still_restores_snapshot");
            }
            fake = new FakeBackend { FailRestore = true };
            using (var controller = new KernelMouseController(fake))
            {
                controller.Apply(800, 1600);
                Check(!controller.Restore(), "restore_failure_reported");
                fake.FailRestore = false;
                Check(controller.Restore() && fake.Restores == 2, "restore_failure_can_retry");
            }
            var registry = new FakeRegistration();
            MouseRegistration.Detach(registry, "DpiFilter");
            Check(registry.Deleted && registry.Filters.SequenceEqual(new[] { "VendorFilter", "mouclass" }), "detach_before_service_delete_preserves_other_filters");
            registry = new FakeRegistration { FailDelete = true };
            try { MouseRegistration.Detach(registry, "DpiFilter"); } catch (IOException) { }
            Check(registry.Filters.SequenceEqual(new[] { "VendorFilter", "mouclass" }), "cleanup_failure_never_restores_blocked_legacy_filter");
            registry = new FakeRegistration { FailDetach = true };
            try { MouseRegistration.Detach(registry, "DpiFilter"); } catch (IOException) { }
            Check(!registry.Deleted, "detach_failure_preserves_driver_service");
            Check(Layout.AddFilter(new[] { "VendorFilter", "mouclass" }).SequenceEqual(new[] { "rawaccel", "VendorFilter", "mouclass" }), "signed_filter_registration_preserves_vendor_and_mouse_driver");
            bool refused = false;
            try { Layout.AddFilter(new[] { "VendorFilter" }); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "missing_windows_mouse_driver_blocks_install");
            var source = new SeriesSource();
            Check(Enumerable.Range(0, 100).All(i => source.Create(1, 0, 1).All(v => v >= 1)), "generated_dpi_never_zero_at_minimum_base");
            Check(!SeriesSource.IsConfigurationValid(1600, 1600, 0), "zero_dpi_range_rejected");
            var randomSource = new ManualRandomSource();
            foreach (int randomBase in new[] { 1, 400, 800, 1600, 2400, 9999 })
            {
                int previousRandom = randomBase;
                bool randomValid = true;
                for (int i = 0; i < 10000; i++)
                {
                    int next = randomSource.Next(previousRandom, randomBase);
                    randomValid &= ManualRandomSource.IsValid(next, randomBase) && Math.Abs(next - randomBase) <= 800 && next >= 1 && next != previousRandom;
                    previousRandom = next;
                }
                Check(randomValid, $"manual_random_selectable_base_{randomBase}_within_800_and_never_zero");
            }
            var scaledConfig = RawAccelBackend.CreateConfig(1600, 3200);
            using (var math = new ManagedAccel(scaledConfig.profiles[0]))
            {
                var half = math.Accelerate(1, -1, 1000.0 / 3200, 1);
                Check(scaledConfig.Errors() == null && half.Item1 == 0.5 && half.Item2 == -0.5, "driver_profile_math_before_packet_quantization");
            }
            foreach (int hardware in new[] { 48000, 65535 })
            {
                var config = RawAccelBackend.CreateConfig(1600, hardware);
                using var math = new ManagedAccel(config.profiles[0]);
                var actual = math.Accelerate(100, -80, 1000.0 / hardware, 1);
                Check(config.Errors() == null && Math.Abs(actual.Item1 - 100.0 * 1600 / hardware) < 1e-8, $"high_hardware_dpi_profile_{hardware}_valid_and_scaled");
            }

            if (args.Length > 1)
            {
                Platform.VerifyDriver(Path.GetFullPath(args[1]));
                Check(true, "approved_driver_hash_and_microsoft_signature_trusted");
                string tampered = Path.Combine(output, "tampered-driver.sys");
                byte[] bytes = File.ReadAllBytes(args[1]);
                bytes[4096] ^= 1;
                File.WriteAllBytes(tampered, bytes);
                refused = false;
                try { Platform.VerifyDriver(tampered); } catch (InvalidDataException) { refused = true; }
                Check(refused, "modified_driver_hash_rejected");
                refused = false;
                try { Platform.VerifySignature(tampered); } catch (InvalidOperationException) { refused = true; }
                Check(refused, "modified_driver_authenticode_rejected");
                File.Delete(tampered);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            fake = new FakeBackend { Delay = new ManualResetEventSlim(false) };
            var state = new AppState(Path.Combine(output, "temporary-state.json"));
            using (var form = new MainForm(new KernelMouseController(fake), state))
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                Exception uiError = null;
                var timeout = new System.Windows.Forms.Timer { Interval = 10000 };
                timeout.Tick += (s, e) => { uiError = new TimeoutException("UI test timed out"); fake.Delay.Set(); Application.ExitThread(); };
                form.Shown += async (s, e) => {
                    try
                    {
                        await Task.Delay(80);
                        Check(fake.Writes == 1, "async_driver_write_started");
                        var label = (Label)typeof(MainForm).GetField("driverLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                        Check(label.Text == "APPLYING DPI...", "ui_pumps_while_driver_delays");
                        fake.Delay.Set();
                        for (int i = 0; i < 50 && label.Text != "DRIVER READY"; i++) await Task.Delay(20);
                        Check(label.Text == "DRIVER READY", "ui_reports_confirmed_write");
                        var advance = typeof(MainForm).GetMethod("AdvanceIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                        Check(await (Task<bool>)advance.Invoke(form, null) && state.Index == 0, "rotation_commits_after_write");
                        fake.FailWrite = true;
                        Check(!await (Task<bool>)advance.Invoke(form, null) && state.Index == 0, "failed_rotation_preserves_saved_index");
                        fake.FailWrite = false;
                        var workflow = typeof(MainForm).GetMethod("ChangeWorkflow", BindingFlags.NonPublic | BindingFlags.Instance);
                        int writes = fake.Writes;
                        Check(state.GameDpiFactor == 1 && !state.PassThrough, "legacy_settings_keep_normal_game_sensitivity");
                        var outputSettings = typeof(MainForm).GetMethod("ChangeOutputSettings", BindingFlags.NonPublic | BindingFlags.Instance);
                        Check(await (Task<bool>)outputSettings.Invoke(form, new object[] { 800, 550, 550 }) && fake.LastBaseline == 800, "selectable_base_calibrates_normal_mouse_dpi");
                        Check(await (Task<bool>)workflow.Invoke(form, new object[] { 2 }) && state.BaseDpi == 800 && state.RandomDpi == 800, "random_mode_accepts_base_other_than_1600");
                        Check(await (Task<bool>)outputSettings.Invoke(form, new object[] { 1600, 550, 550 }) && state.RandomDpi == 1600, "random_base_is_editable_and_resets_to_selected_output");
                        Check(await (Task<bool>)workflow.Invoke(form, new object[] { 1 }), "sequence_mode_still_available_after_base_edit");
                        state.EnablePlus = false;
                        int savedIndex = state.Index;
                        Check(await (Task<bool>)workflow.Invoke(form, new object[] { 2 }) && state.RandomManualMode && state.RandomDpi == 1600, "switch_to_random_mode_starts_at_base");
                        var nextButton = (Button)typeof(MainForm).GetField("nextButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                        var timingButton = (Button)typeof(MainForm).GetField("timingButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                        Check(nextButton.Enabled && nextButton.Text == "CHANGE DPI" && !timingButton.Enabled, "manual_button_works_without_hotkey_and_timer_is_disabled");
                        writes = fake.Writes;
                        typeof(MainForm).GetMethod("ToggleTiming", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                        var timerClock = (DeadlineClock)typeof(MainForm).GetField("clock", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                        timerClock.Start(0, 0, true);
                        typeof(MainForm).GetMethod("WakeTiming", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                        await Task.Delay(80);
                        Check(fake.Writes == writes && !await (Task<bool>)advance.Invoke(form, null), "random_mode_cannot_advance_automatically_even_with_old_auto_setting");
                        timerClock.Clear();
                        nextButton.PerformClick();
                        for (int i = 0; i < 50 && (state.RandomDpi == 1600 || label.Text != "DRIVER READY"); i++) await Task.Delay(20);
                        Check(fake.Writes == writes + 1 && state.RandomDpi != 1600 && ManualRandomSource.IsValid(state.RandomDpi, state.BaseDpi) && fake.LastBaseline == 1600, "one_button_click_applies_one_random_dpi");
                        var randomize = typeof(MainForm).GetMethod("RandomizeDpi", BindingFlags.NonPublic | BindingFlags.Instance);
                        int randomDpi = state.RandomDpi;
                        fake.FailWrite = true;
                        Check(!await (Task<bool>)randomize.Invoke(form, null) && state.RandomDpi == randomDpi, "failed_random_write_preserves_current_dpi");
                        fake.FailWrite = false;
                        var setRandom = typeof(MainForm).GetMethod("SetRandomDpi", BindingFlags.NonPublic | BindingFlags.Instance);
                        writes = fake.Writes;
                        Check(!await (Task<bool>)setRandom.Invoke(form, new object[] { 799 }) && !await (Task<bool>)setRandom.Invoke(form, new object[] { 2401 }) && fake.Writes == writes, "random_mode_rejects_out_of_range_before_io");
                        var applyOverride = typeof(MainForm).GetMethod("ApplyOverride", BindingFlags.NonPublic | BindingFlags.Instance);
                        Check(!await (Task<bool>)applyOverride.Invoke(form, new object[] { 300 }) && fake.Writes == writes, "random_mode_cannot_bypass_range_with_override");
                        Check(await (Task<bool>)setRandom.Invoke(form, new object[] { 1600 }) && fake.LastTarget == 1600, "random_reset_returns_to_1600");
                        var persisted = new JavaScriptSerializer().Deserialize<AppState>(File.ReadAllText(Path.Combine(output, "temporary-state.json")));
                        Check(persisted.RandomManualMode && persisted.RandomDpi == 1600 && persisted.BaseDpi == 1600, "random_mode_and_dpi_are_persisted");
                        Check(await (Task<bool>)workflow.Invoke(form, new object[] { 1 }) && !state.RandomManualMode && state.Index == savedIndex && state.Automatic, "original_sequence_and_auto_setting_survive_random_mode");
                        var gameSetting = typeof(MainForm).GetMethod("SetGameDpiFactor", BindingFlags.NonPublic | BindingFlags.Instance);
                        Check(await (Task<bool>)gameSetting.Invoke(form, new object[] { 2 }) && state.GameDpiFactor == 2 && state.BaseDpi == 1600 && fake.LastTarget == 3200 && fake.LastBaseline == 3200, "half_game_setting_doubles_mouse_and_output_without_added_downscale");
                        Check(await (Task<bool>)advance.Invoke(form, null) && fake.LastBaseline == 3200 && fake.LastTarget == state.Values[state.Index] * 2, "sequence_values_double_for_half_game_sensitivity");
                        Check(await (Task<bool>)applyOverride.Invoke(form, new object[] { 1200 }) && fake.LastBaseline == 3200 && fake.LastTarget == 2400, "override_uses_game_adjusted_output");
                        Check(await (Task<bool>)workflow.Invoke(form, new object[] { 2 }) && fake.LastBaseline == 3200 && fake.LastTarget == 3200, "random_base_is_one_to_one_with_half_game_setting");
                        Check(await (Task<bool>)randomize.Invoke(form, null) && fake.LastTarget == state.RandomDpi * 2, "random_range_doubles_for_half_game_sensitivity");
                        Check(await (Task<bool>)setRandom.Invoke(form, new object[] { 1600 }), "random_reset_returns_to_game_adjusted_base");
                        Check(await (Task<bool>)gameSetting.Invoke(form, new object[] { 4 }) && fake.LastTarget == 6400 && fake.LastBaseline == 6400, "quarter_game_setting_uses_6400_mouse_and_output_for_1600_base");
                        Check(await (Task<bool>)setRandom.Invoke(form, new object[] { 800 }) && fake.LastTarget == 3200 && await (Task<bool>)setRandom.Invoke(form, new object[] { 2400 }) && fake.LastTarget == 9600, "quarter_game_random_endpoints_are_3200_and_9600");
                        Check(await (Task<bool>)setRandom.Invoke(form, new object[] { 1600 }), "quarter_game_reset_is_6400");
                        writes = fake.Writes;
                        Check(!await (Task<bool>)gameSetting.Invoke(form, new object[] { 0 }) && !await (Task<bool>)gameSetting.Invoke(form, new object[] { 3 }) && state.GameDpiFactor == 4 && fake.Writes == writes, "invalid_game_setting_rejected_before_io");
                        fake.FailWrite = true;
                        Check(!await (Task<bool>)gameSetting.Invoke(form, new object[] { 2 }) && state.GameDpiFactor == 4, "failed_game_setting_write_preserves_previous_setting");
                        fake.FailWrite = false;
                        persisted = new JavaScriptSerializer().Deserialize<AppState>(File.ReadAllText(Path.Combine(output, "temporary-state.json")));
                        Check(persisted.GameDpiFactor == 4 && persisted.BaseDpi == 1600, "base_and_game_setting_persist");
                        using (var stateLock = File.Open(Path.Combine(output, "temporary-state.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            Check(!await (Task<bool>)gameSetting.Invoke(form, new object[] { 2 }) && state.GameDpiFactor == 4 && fake.LastBaseline == 6400 && fake.LastTarget == 6400, "game_setting_save_failure_rolls_back_driver_and_setting");
                            Check(!await (Task<bool>)outputSettings.Invoke(form, new object[] { 2400, 550, 550 }) && state.BaseDpi == 1600 && state.RandomDpi == 1600 && fake.LastBaseline == 6400 && fake.LastTarget == 6400, "base_save_failure_rolls_back_output_and_driver");
                            Check(!await (Task<bool>)randomize.Invoke(form, null) && state.RandomDpi == 1600 && fake.LastBaseline == 6400 && fake.LastTarget == 6400, "random_save_failure_rolls_back_to_previous_game_adjusted_output");
                        }
                        Check(await (Task<bool>)workflow.Invoke(form, new object[] { 0 }) && state.PassThrough && fake.Restores == 1, "pass_through_restores_original_configuration");
                        writes = fake.Writes;
                        Check(!await (Task<bool>)advance.Invoke(form, null) && !await (Task<bool>)randomize.Invoke(form, null) && !nextButton.Enabled && !timingButton.Enabled && fake.Writes == writes, "pass_through_disables_timer_button_and_random_changes");
                        Check(await (Task<bool>)gameSetting.Invoke(form, new object[] { 2 }) && fake.Writes == writes, "game_setting_in_pass_through_does_not_write_to_driver");
                        typeof(MainForm).GetField("allowExit", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, true);
                        form.Close();
                    }
                    catch (Exception error) { uiError = error; fake.Delay.Set(); Application.ExitThread(); }
                };
                timeout.Start();
                Application.Run(form);
                timeout.Dispose();
                if (uiError != null) throw uiError;
                Check(fake.Restores == 1, "graceful_exit_restores_before_dispose");
            }
            File.WriteAllText(Path.Combine(output, "safety-tests.json"), new JavaScriptSerializer().Serialize(new { success = true, tests = results, host_driver_installed = false, host_boot_settings_changed = false }));
            Console.WriteLine($"PASS: {results.Count} checks. No driver installed or boot settings changed.");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "safety-tests.json"), new JavaScriptSerializer().Serialize(new { success = false, error = error.ToString(), tests = results }));
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
