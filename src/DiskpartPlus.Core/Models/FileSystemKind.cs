namespace DiskpartPlus.Core.Models;

public enum FileSystemKind
{
    /// <summary>Something Windows doesn't recognise, like APFS or ext4.</summary>
    Unknown,

    /// <summary>No file system at all (RAW).</summary>
    None,

    Ntfs,
    Refs,
    ExFat,
    Fat32,
    Fat,
}

public static class FileSystems
{
    // Characters FAT won't take in a volume label.
    private const string FatForbiddenLabelChars = "*?.,;:/\\|+=<>[]\"";

    // exFAT follows the same rules as file names.
    private const string ExFatForbiddenLabelChars = "\"*/:<>?\\|";

    public static FileSystemKind Parse(string? name) => name?.Trim().ToUpperInvariant() switch
    {
        null or "" or "RAW" => FileSystemKind.None,
        "NTFS" => FileSystemKind.Ntfs,
        "REFS" => FileSystemKind.Refs,
        "EXFAT" => FileSystemKind.ExFat,
        "FAT32" => FileSystemKind.Fat32,
        "FAT" or "FAT16" or "FAT12" => FileSystemKind.Fat,
        _ => FileSystemKind.Unknown,
    };

    public static bool CanFormatTo(this FileSystemKind fileSystem) =>
        fileSystem is FileSystemKind.Ntfs or FileSystemKind.Refs or FileSystemKind.ExFat
            or FileSystemKind.Fat32 or FileSystemKind.Fat;

    public static string DiskpartName(this FileSystemKind fileSystem) => fileSystem switch
    {
        FileSystemKind.Ntfs => "ntfs",
        FileSystemKind.Refs => "refs",
        FileSystemKind.ExFat => "exfat",
        FileSystemKind.Fat32 => "fat32",
        FileSystemKind.Fat => "fat",
        _ => throw new ArgumentOutOfRangeException(nameof(fileSystem), fileSystem, "diskpart can't format to this."),
    };

    public static string DisplayName(this FileSystemKind fileSystem) => fileSystem switch
    {
        FileSystemKind.Ntfs => "NTFS",
        FileSystemKind.Refs => "ReFS",
        FileSystemKind.ExFat => "exFAT",
        FileSystemKind.Fat32 => "FAT32",
        FileSystemKind.Fat => "FAT",
        FileSystemKind.None => "No file system",
        _ => "Unknown",
    };

    public static int MaxLabelLength(this FileSystemKind fileSystem) =>
        fileSystem is FileSystemKind.Ntfs or FileSystemKind.Refs ? 32 : 11;

    /// <summary>Returns the first character this file system won't accept in a label. Null means the label is fine.</summary>
    public static char? FindBadLabelChar(this FileSystemKind fileSystem, string label)
    {
        foreach (var c in label)
        {
            // Quotes would break out of label="..." in the script, so they're never allowed.
            if (char.IsControl(c) || c == '"')
            {
                return c;
            }

            var forbidden = fileSystem switch
            {
                FileSystemKind.Fat32 or FileSystemKind.Fat => FatForbiddenLabelChars,
                FileSystemKind.ExFat => ExFatForbiddenLabelChars,
                _ => string.Empty,
            };

            if (forbidden.Contains(c))
            {
                return c;
            }
        }

        return null;
    }
}
