using System.Runtime.Versioning;
using System.Security.Principal;

namespace DiskpartPlus.Windows;

[SupportedOSPlatform("windows")]
public static class Elevation
{
    /// <summary>True when we're running as administrator, which diskpart needs.</summary>
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
