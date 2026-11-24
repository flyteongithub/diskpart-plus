using DiskpartPlus.Core.Models;

namespace DiskpartPlus.Core.Operations;

/// <summary>
/// One thing the user wants to do, like "format partition 2 as exFAT".
/// Operations are plain data. They get checked by the planner and turned into
/// diskpart commands by the script builder, so the UI never writes diskpart text itself.
/// </summary>
public abstract record DiskOperation
{
    /// <summary>A short, plain sentence for the plan preview.</summary>
    public abstract string Describe();
}

/// <summary>Works on a whole disk.</summary>
public abstract record DiskScopedOperation(int DiskNumber) : DiskOperation;

/// <summary>Works on one partition of a disk.</summary>
public abstract record PartitionScopedOperation(int DiskNumber, int PartitionNumber) : DiskScopedOperation(DiskNumber);

/// <summary>Works on a .vhd or .vhdx file.</summary>
public abstract record VirtualDiskFileOperation(string FilePath) : DiskOperation;

/// <summary>How to format a partition.</summary>
public sealed record FormatSpec(FileSystemKind FileSystem, string Label, bool Quick = true, int? AllocationUnitBytes = null)
{
    public string Describe()
    {
        var label = string.IsNullOrEmpty(Label) ? string.Empty : $" \"{Label}\"";
        return $"{FileSystem.DisplayName()}{label}";
    }
}

/// <summary>Which drive letter to give a partition. A null letter means "the next free one".</summary>
public sealed record DriveAssignment(DriveLetter? Letter)
{
    public static DriveAssignment NextFree { get; } = new((DriveLetter?)null);

    public string Describe() => Letter is { } letter ? $"drive letter {letter.WithColon}" : "the next free drive letter";
}
