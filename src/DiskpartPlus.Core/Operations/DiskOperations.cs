using DiskpartPlus.Core.Models;

namespace DiskpartPlus.Core.Operations;

/// <summary>Removes every partition from the disk. With ZeroEverySector it also writes zeros over the whole thing.</summary>
public sealed record CleanDisk(int DiskNumber, bool ZeroEverySector = false) : DiskScopedOperation(DiskNumber)
{
    public override string Describe() => ZeroEverySector
        ? $"Wipe every sector of disk {DiskNumber} (clean all)"
        : $"Remove all partitions from disk {DiskNumber} (clean)";
}

/// <summary>Switches an empty disk between GPT and MBR.</summary>
public sealed record ConvertPartitionStyle(int DiskNumber, PartitionStyle To) : DiskScopedOperation(DiskNumber)
{
    public override string Describe() => $"Set up disk {DiskNumber} as {To.ToString().ToUpperInvariant()}";
}

public sealed record SetDiskOnline(int DiskNumber, bool Online) : DiskScopedOperation(DiskNumber)
{
    public override string Describe() => Online
        ? $"Bring disk {DiskNumber} online"
        : $"Take disk {DiskNumber} offline";
}

/// <summary>Sets or clears the read-only flag. Clearing it fixes a lot of "write protected" USB sticks.</summary>
public sealed record SetDiskReadOnly(int DiskNumber, bool ReadOnly) : DiskScopedOperation(DiskNumber)
{
    public override string Describe() => ReadOnly
        ? $"Make disk {DiskNumber} read-only"
        : $"Clear the read-only flag on disk {DiskNumber}";
}

/// <summary>
/// Changes the disk's ID. Useful when two disks clash after cloning and Windows keeps one of them offline.
/// GPT disks take a GUID. MBR disks take 8 hex digits.
/// </summary>
public sealed record SetDiskUniqueId(int DiskNumber, string Id) : DiskScopedOperation(DiskNumber)
{
    public override string Describe() => $"Change the ID of disk {DiskNumber} to {Id}";
}

/// <summary>Creates a partition. It can also format it and give it a letter in the same go.</summary>
public sealed record CreatePartition(
    int DiskNumber,
    NewPartitionKind Kind = NewPartitionKind.Primary,
    ByteSize? Size = null,
    FormatSpec? Format = null,
    DriveAssignment? Assign = null) : DiskScopedOperation(DiskNumber)
{
    public override string Describe()
    {
        var kind = Kind switch
        {
            NewPartitionKind.Efi => "EFI",
            NewPartitionKind.MicrosoftReserved => "Microsoft reserved",
            _ => "primary",
        };

        var size = Size is { } s ? $"{s} {kind} partition" : $"{kind} partition using the rest of the space";
        var text = $"Create a {size} on disk {DiskNumber}";

        if (Format is not null)
        {
            text += $", formatted {Format.Describe()}";
        }

        if (Assign is not null)
        {
            text += $", with {Assign.Describe()}";
        }

        return text;
    }
}
