using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Planning;

namespace DiskpartPlus.Core.Safety;

/// <summary>
/// Decides how risky an operation is and blocks the few things that would break the running PC.
/// It stays quiet otherwise. People using this know what diskpart does.
/// </summary>
/// <remarks>
/// The hard rule: nothing destructive touches the disk Windows runs from, the disk it boots from,
/// a disk with the page file on it or the disk Diskpart+ is running from. diskpart refuses most of
/// these anyway, this just says so up front.
/// </remarks>
public static class SafetyPolicy
{
    public static RiskLevel RiskOf(DiskOperation operation) => operation switch
    {
        CleanDisk { ZeroEverySector: true } => RiskLevel.Critical,
        CleanDisk or FormatPartition or DeletePartition => RiskLevel.Destructive,

        ConvertPartitionStyle
            or CreatePartition
            or ExtendPartition
            or ShrinkPartition
            or SetPartitionType
            or SetGptAttributes
            or SetDiskUniqueId
            or RemoveDriveLetter
            or DetachVirtualDisk
            or SetDiskOnline { Online: false }
            or SetDiskReadOnly { ReadOnly: true } => RiskLevel.Caution,

        _ => RiskLevel.Safe,
    };

    internal static void Check(DiskOperation operation, PlanState state, List<PlanIssue> issues)
    {
        switch (operation)
        {
            case CleanDisk clean:
                if (BlockIfCriticalDisk(clean, state, issues) is { } disk
                    && clean.ZeroEverySector
                    && (disk.Media == MediaType.Ssd || disk.Bus == BusType.Nvme))
                {
                    issues.Add(new PlanIssue(IssueSeverity.Warning, IssueCodes.CleanAllOnSsd,
                        "On an SSD, clean all doesn't reliably erase everything and adds wear. The maker's secure erase tool does a proper job.") { Operation = clean });
                }

                break;

            case ConvertPartitionStyle or SetDiskUniqueId or SetDiskOnline { Online: false } or SetDiskReadOnly { ReadOnly: true }:
                BlockIfCriticalDisk((DiskScopedOperation)operation, state, issues);
                break;

            case FormatPartition or DeletePartition or SetPartitionType or SetGptAttributes:
                BlockIfCriticalPartition((PartitionScopedOperation)operation, state, issues, p => p.IsCritical);
                break;

            case RemoveDriveLetter remove:
                BlockIfCriticalPartition(remove, state, issues, p => p.IsBoot || p.HoldsPagefile || p.HostsApplication);
                break;

            case AssignDriveLetter { Replacing: not null } change:
                BlockIfCriticalPartition(change, state, issues, p => p.IsBoot || p.HoldsPagefile || p.HostsApplication);
                break;

            case DetachVirtualDisk detach when state.Snapshot.FindVirtualDisk(detach.FilePath) is { IsCritical: true } vhd:
                issues.Add(Blocker(IssueCodes.CriticalDisk, $"Disk {vhd.Number} {WhyCritical(vhd)}.", detach));
                break;
        }
    }

    /// <summary>Destructive work on more than one disk at once makes mistakes too easy.</summary>
    internal static void CheckWholePlan(IReadOnlyList<DiskOperation> operations, List<PlanIssue> issues)
    {
        var destroyedDisks = operations
            .Where(o => RiskOf(o) >= RiskLevel.Destructive)
            .OfType<DiskScopedOperation>()
            .Select(o => o.DiskNumber)
            .Distinct()
            .Count();

        if (destroyedDisks > 1)
        {
            issues.Add(new PlanIssue(IssueSeverity.Blocker, IssueCodes.MultipleDisks, "Erase one disk at a time."));
        }
    }

    private static DiskInfo? BlockIfCriticalDisk(DiskScopedOperation operation, PlanState state, List<PlanIssue> issues)
    {
        var disk = state.ResolveDisk(operation, null);
        if (disk is { IsCritical: true })
        {
            issues.Add(Blocker(IssueCodes.CriticalDisk, $"Disk {disk.Number} {WhyCritical(disk)}.", operation));
            return null;
        }

        return disk;
    }

    private static void BlockIfCriticalPartition(PartitionScopedOperation operation, PlanState state, List<PlanIssue> issues, Func<PartitionInfo, bool> isCritical)
    {
        if (state.ResolvePartition(operation, null) is { } partition && isCritical(partition))
        {
            var name = partition.Letter is { } letter ? letter.WithColon : $"Partition {partition.Number} on disk {partition.DiskNumber}";
            issues.Add(Blocker(IssueCodes.CriticalPartition, $"{name} {WhyCritical(partition)}.", operation));
        }
    }

    private static string WhyCritical(DiskInfo disk) => disk switch
    {
        { IsBoot: true } => "is the Windows disk",
        { IsSystem: true } => "has this PC's boot files",
        { HoldsPagefile: true } => "has the page file",
        _ => "is where Diskpart+ is running from",
    };

    private static string WhyCritical(PartitionInfo partition) => partition switch
    {
        { IsBoot: true } => "is the running Windows",
        { IsSystem: true } => "has this PC's boot files",
        { HoldsPagefile: true } => "has the page file",
        _ => "is where Diskpart+ is running from",
    };

    private static PlanIssue Blocker(string code, string message, DiskOperation operation) =>
        new(IssueSeverity.Blocker, code, message) { Operation = operation };
}
