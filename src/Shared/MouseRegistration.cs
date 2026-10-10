using System;
using Microsoft.Win32;
namespace Dpi.Setup;
internal interface IMouseRegistration
{
    string[] ReadFilters();
    void WriteFilters(string[] filters);
    void DeleteService(string name);
}
internal sealed class WindowsMouseRegistration : IMouseRegistration
{
    public string[] ReadFilters() => Layout.Filters();
    public void WriteFilters(string[] filters) => Layout.SetFilters(filters);
    public void DeleteService(string name) => Registry.LocalMachine.DeleteSubKeyTree(
        "SYSTEM\\CurrentControlSet\\Services\\" + name, false);
}
internal static class MouseRegistration
{
    public static void Detach(IMouseRegistration registry, string name)
    {
        // Never delete/disable a service before removing its class registration.
        // Never restore a blocked legacy filter if later cleanup fails.
        registry.WriteFilters(Layout.RemoveFilter(registry.ReadFilters(), name));
        registry.DeleteService(name);
    }
}
