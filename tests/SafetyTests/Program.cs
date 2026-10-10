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
        public bool FailCapture, FailWrite, FailRestore;
        public ManualResetEventSlim Delay;
        public object Capture() { Captures++; if (FailCapture) throw new IOException("missing driver"); return Snapshot; }
        public void Apply(int target, int baseline) { Writes++; Delay?.Wait(); if (FailWrite) throw new IOException("write failed"); }
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
                Check(!controller.Apply(3201, 1600) && !controller.Apply(1, 0), "out_of_range_rejected_before_io");
                Check(controller.Apply(800, 1600) && controller.Apply(2400, 1600) && fake.Captures == 1, "capture_original_once");
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
