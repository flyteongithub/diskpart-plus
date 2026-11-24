using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Safety;

namespace DiskpartPlus.Core.Planning;

/// <summary>
/// Keeps track of what earlier steps in a plan will have done by the time we reach later ones.
/// For example, after "clean" the old partitions are gone and the disk is RAW, so converting it
/// to GPT is fine, but formatting "partition 2" isn't.
/// </summary>
internal sealed class PlanState(PlanningContext context)
{
    // Room diskpart keeps for alignment and partition tables on a freshly cleaned disk.
    private static readonly ByteSize Overhead = ByteSize.FromMegabytes(2);

    private readonly HashSet<int> _cleanedDisks = [];
    private readonly Dictionary<int, PartitionStyle> _styleChanges = [];
    private readonly HashSet<(int Disk, int Partition)> _deletedPartitions = [];
    private readonly HashSet<DriveLetter> _claimedLetters = [];
    private readonly HashSet<DriveLetter> _releasedLetters = [];
    private readonly Dictionary<int, ByteSize> _usedByNewPartitions = [];

    public PlanningContext Context { get; } = context;

    public StorageSnapshot Snapshot => Context.Snapshot;

    public bool WasCleaned(int diskNumber) => _cleanedDisks.Contains(diskNumber);

    public bool IsEmpty(DiskInfo disk) =>
        WasCleaned(disk.Number) || disk.Partitions.All(p => _deletedPartitions.Contains((disk.Number, p.Number)));

    public PartitionStyle StyleOf(DiskInfo disk)
    {
        if (_styleChanges.TryGetValue(disk.Number, out var style))
        {
            return style;
        }

        return WasCleaned(disk.Number) ? PartitionStyle.Raw : disk.Style;
    }

    public ByteSize FreeSpaceOn(DiskInfo disk)
    {
        var free = WasCleaned(disk.Number) ? disk.Size - Overhead : disk.LargestFreeExtent;
        if (_usedByNewPartitions.TryGetValue(disk.Number, out var used))
        {
            free -= used;
        }

        return ByteSize.Max(free, ByteSize.Zero);
    }

    public bool IsLetterTaken(DriveLetter letter) =>
        _claimedLetters.Contains(letter)
        || (!_releasedLetters.Contains(letter) && Snapshot.IsLetterInUse(letter));

    /// <summary>
    /// Finds the disk an operation points at. Adds a blocker and returns null if it isn't there.
    /// </summary>
    public DiskInfo? ResolveDisk(DiskScopedOperation operation, List<PlanIssue>? issues)
    {
        var disk = Snapshot.FindDisk(operation.DiskNumber);
        if (disk is null)
        {
            issues?.Add(new PlanIssue(IssueSeverity.Blocker, IssueCodes.DiskNotFound,
                $"There's no disk {operation.DiskNumber}. Refresh the disk list and try again.") { Operation = operation });
        }

        return disk;
    }

    /// <summary>
    /// Finds the partition an operation points at, taking earlier steps into account.
    /// Adds a blocker and returns null if it isn't there or won't be by then.
    /// </summary>
    public PartitionInfo? ResolvePartition(PartitionScopedOperation operation, List<PlanIssue>? issues)
    {
        var disk = ResolveDisk(operation, issues);
        if (disk is null)
        {
            return null;
        }

        if (WasCleaned(disk.Number) || _deletedPartitions.Contains((disk.Number, operation.PartitionNumber)))
        {
            issues?.Add(new PlanIssue(IssueSeverity.Blocker, IssueCodes.PartitionGone,
                $"Partition {operation.PartitionNumber} on disk {disk.Number} will already be gone by this step.") { Operation = operation });
            return null;
        }

        var partition = disk.FindPartition(operation.PartitionNumber);
        if (partition is null)
        {
            issues?.Add(new PlanIssue(IssueSeverity.Blocker, IssueCodes.PartitionNotFound,
                $"Disk {disk.Number} has no partition {operation.PartitionNumber}.") { Operation = operation });
        }

        return partition;
    }

    /// <summary>Records what an operation will have changed, so the next ones are checked against that.</summary>
    public void Apply(DiskOperation operation)
    {
        switch (operation)
        {
            case CleanDisk clean:
                _cleanedDisks.Add(clean.DiskNumber);
                _styleChanges.Remove(clean.DiskNumber);
                _usedByNewPartitions.Remove(clean.DiskNumber);
                ReleaseLettersOn(clean.DiskNumber);
                break;

            case ConvertPartitionStyle convert:
                _styleChanges[convert.DiskNumber] = convert.To;
                break;

            case CreatePartition create when Snapshot.FindDisk(create.DiskNumber) is { } disk:
                // Creating a partition on a RAW disk sets it up as MBR.
                if (StyleOf(disk) == PartitionStyle.Raw)
                {
                    _styleChanges[disk.Number] = PartitionStyle.Mbr;
                }

                var size = create.Size ?? FreeSpaceOn(disk);
                _usedByNewPartitions[disk.Number] = _usedByNewPartitions.GetValueOrDefault(disk.Number) + size;
                Claim(create.Assign?.Letter);
                break;

            case DeletePartition delete:
                _deletedPartitions.Add((delete.DiskNumber, delete.PartitionNumber));
                Release(Snapshot.FindPartition(delete.DiskNumber, delete.PartitionNumber)?.Letter);
                break;

            case AssignDriveLetter assign:
                Release(assign.Replacing);
                Claim(assign.Letter);
                break;

            case RemoveDriveLetter remove:
                Release(remove.Letter);
                break;

            case CreateVirtualDisk { Setup.Assign: { } assignment }:
                Claim(assignment.Letter);
                break;
        }
    }

    private void ReleaseLettersOn(int diskNumber)
    {
        foreach (var partition in Snapshot.FindDisk(diskNumber)?.Partitions ?? [])
        {
            Release(partition.Letter);
        }
    }

    private void Claim(DriveLetter? letter)
    {
        if (letter is { } l)
        {
            _claimedLetters.Add(l);
            _releasedLetters.Remove(l);
        }
    }

    private void Release(DriveLetter? letter)
    {
        if (letter is { } l)
        {
            _releasedLetters.Add(l);
            _claimedLetters.Remove(l);
        }
    }
}
