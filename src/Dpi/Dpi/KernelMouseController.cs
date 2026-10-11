using System;
namespace Dpi;
internal interface IMouseBackend
{
    object Capture();
    void Apply(int target, int baseline);
    void Restore(object snapshot);
}
internal sealed class RawAccelBackend : IMouseBackend
{
    public object Capture()
    {
        VersionHelper.ValidOrThrow();
        return DriverConfig.GetActive();
    }
    internal static DriverConfig CreateConfig(int target, int baseline)
    {
        var profile = new Profile { name = "DPI Modifier", outputDPI = target };
        profile.argsX.mode = AccelMode.noaccel;
        profile.argsY.mode = AccelMode.noaccel;
        var config = DriverConfig.FromProfile(profile);
        config.defaultDeviceConfig.dpi = baseline;
        return config;
    }
    public void Apply(int target, int baseline) => CreateConfig(target, baseline).Activate();


    public void Restore(object snapshot) => ((DriverConfig)snapshot).Activate();
}
internal sealed class KernelMouseController : IDisposable
{
    private readonly IMouseBackend backend;
    private readonly object gate = new object();
    private object original;
    private bool changed;
    private bool disposed;
    public int LastError { get; private set; }
    public string LastErrorMessage { get; private set; }
    public bool InputRejected { get; private set; }
    public KernelMouseController() : this(new RawAccelBackend()) { }
    internal KernelMouseController(IMouseBackend backend) => this.backend = backend;
    public bool Apply(int target, int baseline)
    {
        lock (gate)
        {
            InputRejected = !GameDpiSettings.IsMouseDpiValid(baseline) || !GameDpiSettings.IsOutputDpiValid(target);
            if (InputRejected || disposed) { LastError = InputRejected ? 87 : 6; return false; }
            try
            {
                if (original == null) original = backend.Capture();
                changed = true;
                backend.Apply(target, baseline);
                LastError = 0;
                LastErrorMessage = null;
                return true;
            }
            catch (Exception error) { LastError = 31; LastErrorMessage = error.Message; return false; }
        }
    }
    public bool Restore()
    {
        lock (gate)
        {
            if (!changed) return true;
            try { backend.Restore(original); changed = false; LastError = 0; return true; }
            catch (Exception error) { LastError = 31; LastErrorMessage = error.Message; return false; }
        }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; Restore(); disposed = true; }
    }
}
