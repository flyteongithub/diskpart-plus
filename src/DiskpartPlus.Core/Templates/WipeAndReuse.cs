using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;

namespace DiskpartPlus.Core.Templates;

public sealed record WipeAndReuseOptions
{
    public PartitionStyle Style { get; init; } = PartitionStyle.Gpt;

    /// <summary>exFAT by default because Windows and macOS can both read and write it.</summary>
    public FileSystemKind FileSystem { get; init; } = FileSystemKind.ExFat;

    public string Label { get; init; } = "DATA";

    /// <summary>Null means don't give it a letter.</summary>
    public DriveAssignment? Assign { get; init; } = DriveAssignment.NextFree;

    /// <summary>Use "clean all" instead of "clean". Much slower.</summary>
    public bool ZeroEverySector { get; init; }
}

/// <summary>
/// The "make this drive usable again" button. Clears everything off a disk (a Mac drive,
/// an old Linux install, a USB stick with odd leftover partitions) and gives you one
/// clean partition that fills the whole thing.
/// </summary>
public static class WipeAndReuse
{
    public static IReadOnlyList<DiskOperation> Build(int diskNumber, WipeAndReuseOptions? options = null)
    {
        options ??= new WipeAndReuseOptions();

        return
        [
            new CleanDisk(diskNumber, options.ZeroEverySector),
            new ConvertPartitionStyle(diskNumber, options.Style),
            new CreatePartition(
                diskNumber,
                NewPartitionKind.Primary,
                Size: null,
                Format: new FormatSpec(options.FileSystem, options.Label),
                Assign: options.Assign),
        ];
    }
}
