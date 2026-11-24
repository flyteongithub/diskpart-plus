namespace DiskpartPlus.Core.Models;

public sealed record VolumeInfo
{
    public string? Label { get; init; }

    public required FileSystemKind FileSystem { get; init; }

    /// <summary>The file system name exactly as Windows reported it.</summary>
    public string? FileSystemName { get; init; }

    public ByteSize Size { get; init; }

    public ByteSize FreeSpace { get; init; }

    public bool IsBitLockerProtected { get; init; }
}
