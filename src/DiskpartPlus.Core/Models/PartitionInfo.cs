using DiskpartPlus.Core.Partitions;

namespace DiskpartPlus.Core.Models;

public sealed record PartitionInfo
{
    public required int DiskNumber { get; init; }

    /// <summary>The partition number diskpart uses for "select partition".</summary>
    public required int Number { get; init; }

    public required ByteSize Size { get; init; }

    public required PartitionType Type { get; init; }

    public ByteSize Offset { get; init; }

    public bool IsSystem { get; init; }

    public bool IsBoot { get; init; }

    public bool IsActive { get; init; }

    public bool IsHidden { get; init; }

    public bool HoldsPagefile { get; init; }

    /// <summary>Diskpart+ itself is running from this partition.</summary>
    public bool HostsApplication { get; init; }

    public GptAttributeFlags GptAttributes { get; init; }

    public DriveLetter? Letter { get; init; }

    /// <summary>Null when Windows can't read what's on it (APFS, ext4 and so on).</summary>
    public VolumeInfo? Volume { get; init; }

    public ByteSize End => Offset + Size;

    /// <summary>True when deleting or formatting this partition would break the running PC.</summary>
    public bool IsCritical => IsSystem || IsBoot || HoldsPagefile || HostsApplication;

    public FileSystemKind FileSystem => Volume?.FileSystem ?? FileSystemKind.Unknown;

    public string DisplayName
    {
        get
        {
            var name = Volume?.Label is { Length: > 0 } label ? label : Type.Name;
            return Letter is { } letter ? $"{letter.WithColon} {name}" : name;
        }
    }
}
