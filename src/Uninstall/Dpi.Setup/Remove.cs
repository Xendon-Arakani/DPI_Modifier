using System;
using System.Reflection;
namespace Dpi.Setup;
internal static class Remove
{
    public static void Apply()
    {
        if (!Platform.IsAdministrator) throw new UnauthorizedAccessException("Administrator approval is required.");
        if (!string.Equals(Assembly.GetExecutingAssembly().Location, Layout.RemovalPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run the installed uninstaller.");
        Removal.Apply(Installation.Load());
    }
}
