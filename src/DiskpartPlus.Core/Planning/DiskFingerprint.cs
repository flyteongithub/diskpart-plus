using DiskpartPlus.Core.Models;

namespace DiskpartPlus.Core.Planning;

/// <summary>
/// Enough about a disk to tell whether "disk 2" right now is the same drive as "disk 2" when the
/// plan was made. Disk numbers can shift when drives are plugged in or removed. diskpart
/// can only select by number, so this check runs right before every script.
/// </summary>
public sealed record DiskFingerprint(int Number, string FriendlyName, ByteSize Size, string? UniqueId, string? SerialNumber)
{
    public static DiskFingerprint From(DiskInfo disk) =>
        new(disk.Number, disk.FriendlyName, disk.Size, disk.UniqueId, disk.SerialNumber);

    public bool Matches(DiskInfo disk)
    {
        if (disk.Number != Number || disk.Size != Size)
        {
            return false;
        }

        // The unique ID is the best thing to go on when both sides have one.
        if (HasValue(UniqueId) && HasValue(disk.UniqueId))
        {
            return Same(UniqueId, disk.UniqueId);
        }

        // Some USB enclosures don't report one, so fall back to serial number and name.
        if (HasValue(SerialNumber) && HasValue(disk.SerialNumber) && !Same(SerialNumber, disk.SerialNumber))
        {
            return false;
        }

        return Same(FriendlyName, disk.FriendlyName);
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
