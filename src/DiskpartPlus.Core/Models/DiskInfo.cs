using DiskpartPlus.Core.Partitions;

namespace DiskpartPlus.Core.Models;

/// <summary>Everything we know about one disk at the moment we looked.</summary>
public sealed record DiskInfo
{
    /// <summary>The number diskpart uses. Careful: this can change when drives are plugged in or removed.</summary>
    public required int Number { get; init; }

    public required string FriendlyName { get; init; }

    public required ByteSize Size { get; init; }

    /// <summary>A stable ID for the physical disk. This is what we use to make sure "disk 2" is still the same drive.</summary>
    public string? UniqueId { get; init; }

    public string? SerialNumber { get; init; }

    public PartitionStyle Style { get; init; }

    public BusType Bus { get; init; }

    public MediaType Media { get; init; }

    public ByteSize LargestFreeExtent { get; init; }

    /// <summary>Holds the system (EFI) partition this PC booted from.</summary>
    public bool IsSystem { get; init; }

    /// <summary>Holds the running copy of Windows.</summary>
    public bool IsBoot { get; init; }

    public bool HoldsPagefile { get; init; }

    /// <summary>Diskpart+ itself is running from this disk.</summary>
    public bool HostsApplication { get; init; }

    public bool IsReadOnly { get; init; }

    public bool IsOffline { get; init; }

    /// <summary>For virtual disks this is the path of the .vhd or .vhdx file.</summary>
    public string? Location { get; init; }

    public IReadOnlyList<PartitionInfo> Partitions { get; init; } = [];

    public bool IsVirtual => Bus is BusType.FileBackedVirtual or BusType.Virtual;

    /// <summary>True when wiping this disk would break the running PC.</summary>
    public bool IsCritical => IsSystem || IsBoot || HoldsPagefile || HostsApplication;

    public bool HasApplePartitions => Partitions.Any(p => p.Type.Family == PartitionFamily.Apple);

    public string DisplayName => $"Disk {Number}: {FriendlyName} ({Size})";

    public PartitionInfo? FindPartition(int partitionNumber) =>
        Partitions.FirstOrDefault(p => p.Number == partitionNumber);
}
