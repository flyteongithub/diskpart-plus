namespace DiskpartPlus.Core.Models;

/// <summary>
/// A picture of every disk at one point in time. Plans are checked against a snapshot,
/// and then checked again against a fresh one right before anything runs.
/// </summary>
public sealed record StorageSnapshot
{
    public required IReadOnlyList<DiskInfo> Disks { get; init; }

    /// <summary>Letters taken by things that aren't partitions, like network drives and subst.</summary>
    public IReadOnlyCollection<DriveLetter> OtherLettersInUse { get; init; } = [];

    public DateTimeOffset TakenAt { get; init; } = DateTimeOffset.Now;

    public DiskInfo? FindDisk(int diskNumber) =>
        Disks.FirstOrDefault(d => d.Number == diskNumber);

    public PartitionInfo? FindPartition(int diskNumber, int partitionNumber) =>
        FindDisk(diskNumber)?.FindPartition(partitionNumber);

    public DiskInfo? FindVirtualDisk(string filePath) =>
        Disks.FirstOrDefault(d => d.IsVirtual && SamePath(d.Location, filePath));

    /// <summary>Free drive letters from C to Z. A and B are left out because Windows keeps them for floppy drives.</summary>
    public IEnumerable<DriveLetter> AvailableLetters() =>
        Enumerable.Range('C', 'Z' - 'C' + 1)
            .Select(c => new DriveLetter((char)c))
            .Where(l => !IsLetterInUse(l));

    public bool IsLetterInUse(DriveLetter letter) =>
        OtherLettersInUse.Contains(letter)
        || Disks.Any(d => d.Partitions.Any(p => p.Letter == letter));

    // Windows paths aren't case sensitive and either slash works.
    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalise(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');
}
