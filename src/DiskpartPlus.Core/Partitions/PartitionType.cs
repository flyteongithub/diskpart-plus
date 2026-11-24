using DiskpartPlus.Core.Models;

namespace DiskpartPlus.Core.Partitions;

public enum PartitionFamily
{
    Unknown,
    Windows,
    Apple,
    Linux,
    Other,
}

/// <summary>
/// What a partition is for, based on its GPT type GUID or MBR type byte.
/// This is how we can say "APFS container" when Windows itself just shows a blank.
/// </summary>
public sealed record PartitionType
{
    public required string Name { get; init; }

    public required PartitionFamily Family { get; init; }

    public Guid? GptId { get; init; }

    public byte? MbrId { get; init; }

    /// <summary>
    /// True for the ordinary data types diskpart is happy to delete.
    /// Everything else (EFI, recovery, Apple, Linux and so on) needs "delete partition override".
    /// </summary>
    public bool IsDataPartition { get; init; }

    /// <summary>A shorter name for tight spaces, like "MSR". Falls back to Name.</summary>
    public string? ShortName { get; init; }

    /// <summary>A short note we can show in the UI, like "Holds the boot loader".</summary>
    public string? Notes { get; init; }

    public bool NeedsOverrideToDelete => !IsDataPartition;

    /// <summary>The value diskpart wants after "set id=".</summary>
    public string? DiskpartId(PartitionStyle style) => style switch
    {
        PartitionStyle.Gpt => GptId?.ToString("D"),
        PartitionStyle.Mbr => MbrId?.ToString("X2"),
        _ => null,
    };

    public override string ToString() => Name;
}
