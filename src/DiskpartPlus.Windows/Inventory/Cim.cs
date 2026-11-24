using System.Management;
using System.Runtime.Versioning;

namespace DiskpartPlus.Windows.Inventory;

/// <summary>Small helpers for pulling typed values out of WMI objects without blowing up on missing ones.</summary>
[SupportedOSPlatform("windows")]
internal static class Cim
{
    public static List<ManagementBaseObject> Query(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        return results.Cast<ManagementBaseObject>().ToList();
    }

    // Some properties don't exist on older builds of Windows. Treat those as empty.
    public static object? Get(ManagementBaseObject obj, string name)
    {
        try
        {
            return obj[name];
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    public static string? String(ManagementBaseObject obj, string name) =>
        Get(obj, name)?.ToString() is { Length: > 0 } s ? s.Trim() : null;

    public static long Long(ManagementBaseObject obj, string name) => Get(obj, name) switch
    {
        null => 0,
        ulong u => u > long.MaxValue ? long.MaxValue : (long)u,
        var v => Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture),
    };

    public static int Int(ManagementBaseObject obj, string name) => (int)Math.Clamp(Long(obj, name), int.MinValue, int.MaxValue);

    public static bool Bool(ManagementBaseObject obj, string name) => Get(obj, name) is true;

    public static char? Letter(ManagementBaseObject obj, string name)
    {
        var c = Get(obj, name) switch
        {
            char ch => ch,
            ushort u => (char)u,
            string { Length: > 0 } s => s[0],
            _ => '\0',
        };

        return char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : null;
    }

    public static string[] Strings(ManagementBaseObject obj, string name) =>
        Get(obj, name) as string[] ?? [];
}
