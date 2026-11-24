using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Partitions;

namespace DiskpartPlus.Core.Operations;

public sealed record FormatPartition(int DiskNumber, int PartitionNumber, FormatSpec Format)
    : PartitionScopedOperation(DiskNumber, PartitionNumber)
{
    public override string Describe() =>
        $"Format disk {DiskNumber} partition {PartitionNumber} as {Format.Describe()}";
}

/// <summary>
/// Deletes a partition. Override is needed for anything that isn't a plain data partition,
/// like EFI, recovery or a Mac partition. That's the case Disk Management greys out.
/// </summary>
public sealed record DeletePartition(int DiskNumber, int PartitionNumber, bool Override = false)
    : PartitionScopedOperation(DiskNumber, PartitionNumber)
{
    public override string Describe() =>
        $"Delete disk {DiskNumber} partition {PartitionNumber}" + (Override ? " (override)" : string.Empty);
}

/// <summary>Grows a partition into the free space straight after it. A null size means "all of it".</summary>
public sealed record ExtendPartition(int DiskNumber, int PartitionNumber, ByteSize? By = null)
    : PartitionScopedOperation(DiskNumber, PartitionNumber)
{
    public override string Describe() => By is { } by
        ? $"Grow disk {DiskNumber} partition {PartitionNumber} by {by}"
        : $"Grow disk {DiskNumber} partition {PartitionNumber} into all the free space after it";
}

public sealed record ShrinkPartition(int DiskNumber, int PartitionNumber, ByteSize By)
    : PartitionScopedOperation(DiskNumber, PartitionNumber)
{
    public override string Describe() => $"Shrink disk {DiskNumber} partition {PartitionNumber} by {By}";
}

/// <summary>
/// Gives a partition a letter. Set Replacing when it already has one and you want to change it.
/// A null Letter means "the next free one".
/// </summary>
public sealed record AssignDriveLetter(int DiskNumber, int PartitionNumber, DriveLetter? Letter = null, DriveLetter? Replacing = null)
    : PartitionScopedOperation(DiskNumber, PartitionNumber)
{
    public override string Describe()
    {
        var target = Letter is { } letter ? letter.WithColon : "the next free letter";
        return Replacing is { } old
            ? $"Change {old.WithColon} on disk {DiskNumber} partition {PartitionNumber} to {target}"
            : $"Give disk {DiskNumber} partition {PartitionNumber} {target}";
    }
}

public sealed record RemoveDriveLetter(int DiskNumber, int PartitionNumber, DriveLetter Letter)
    : PartitionScopedOperation(DiskNumber, PartitionNumber)
{
    public override string Describe() =>
        $"Remove {Letter.WithColon} from disk {DiskNumber} partition {PartitionNumber}";
}

public sealed record SetPartitionType(int DiskNumber, int PartitionNumber, PartitionType Type)
    : PartitionScopedOperation(DiskNumber, PartitionNumber)
{
    public override string Describe() =>
        $"Change the type of disk {DiskNumber} partition {PartitionNumber} to {Type.Name}";
}

public sealed record SetGptAttributes(int DiskNumber, int PartitionNumber, GptAttributeFlags Attributes)
    : PartitionScopedOperation(DiskNumber, PartitionNumber)
{
    public override string Describe() =>
        $"Set GPT attributes on disk {DiskNumber} partition {PartitionNumber} to 0x{(ulong)Attributes:X16}";
}
