using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Planning;

namespace DiskpartPlus.Core.Safety;

/// <summary>
/// Decides how risky an operation is and whether Diskpart+ should allow it at all.
/// Every rule here is a plain function of the operation and the disk snapshot, so it's easy to test.
/// </summary>
/// <remarks>
/// The hard rule: nothing destructive ever touches the disk Windows is running from,
/// the disk it boots from, a disk with the page file on it or the disk Diskpart+ is running from.
/// There's no "I know what I'm doing" switch for that.
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
                CheckClean(clean, state, issues);
                break;

            case ConvertPartitionStyle or SetDiskUniqueId or SetDiskOnline { Online: false } or SetDiskReadOnly { ReadOnly: true }:
                BlockIfCriticalDisk((DiskScopedOperation)operation, state, issues);
                break;

            case SetDiskReadOnly { ReadOnly: false } clear:
                issues.Add(Info(IssueCodes.ReadOnlyHardware,
                    "If the drive has a lock switch or has worn out and locked itself, this won't help.", clear));
                break;

            case FormatPartition or DeletePartition:
                CheckDestroyPartition((PartitionScopedOperation)operation, state, issues);
                break;

            case SetPartitionType or SetGptAttributes:
                CheckPartitionMetadata((PartitionScopedOperation)operation, state, issues);
                break;

            case RemoveDriveLetter remove:
                CheckRemoveLetter(remove, state, issues);
                break;

            case AssignDriveLetter { Replacing: not null } change:
                CheckRemoveLetter(change, state, issues);
                break;

            case DetachVirtualDisk detach:
                CheckDetach(detach, state, issues);
                break;
        }
    }

    /// <summary>Destructive work on more than one disk at once makes mistakes too easy, so it isn't allowed.</summary>
    internal static void CheckWholePlan(IReadOnlyList<DiskOperation> operations, List<PlanIssue> issues)
    {
        var destroyedDisks = operations
            .Where(o => RiskOf(o) >= RiskLevel.Destructive)
            .OfType<DiskScopedOperation>()
            .Select(o => o.DiskNumber)
            .Distinct()
            .ToList();

        if (destroyedDisks.Count > 1)
        {
            issues.Add(new PlanIssue(IssueSeverity.Blocker, IssueCodes.MultipleDisks,
                $"This plan erases data on disks {string.Join(" and ", destroyedDisks)}. Do one disk at a time."));
        }
    }

    private static void CheckClean(CleanDisk clean, PlanState state, List<PlanIssue> issues)
    {
        if (BlockIfCriticalDisk(clean, state, issues) is not { } disk)
        {
            return;
        }

        if (disk.HasApplePartitions)
        {
            issues.Add(Info(IssueCodes.ApplePartitions,
                "This disk has Mac partitions. Windows can't read them, so Diskpart+ can't show what's on them. Back up anything you need on a Mac first.", clean));
        }

        WarnAboutVolumes(disk.Partitions, clean, issues);

        if (clean.ZeroEverySector)
        {
            issues.Add(Info(IssueCodes.CleanAllSlow,
                $"This writes zeros to all {disk.Size} and can take hours. You can't use the disk until it's done.", clean));

            if (disk.Media == MediaType.Ssd || disk.Bus == BusType.Nvme)
            {
                issues.Add(Warning(IssueCodes.CleanAllOnSsd,
                    "On an SSD this doesn't reliably erase everything and it wears the drive. For a proper wipe, use the maker's secure erase tool.", clean));
            }
        }
    }

    private static void CheckDestroyPartition(PartitionScopedOperation operation, PlanState state, List<PlanIssue> issues)
    {
        if (state.ResolvePartition(operation, null) is not { } partition || state.Snapshot.FindDisk(operation.DiskNumber) is not { } disk)
        {
            return;
        }

        if (partition.IsCritical)
        {
            issues.Add(Blocker(IssueCodes.CriticalPartition, $"{Describe(partition)} {WhyCritical(partition)}. Diskpart+ won't erase it.", operation));
            return;
        }

        if (disk.IsCritical)
        {
            issues.Add(Warning(IssueCodes.OnWindowsDisk,
                "This partition is on the same disk as Windows. Double-check it's the one you mean.", operation));
        }

        if (operation is DeletePartition && partition.Type.NeedsOverrideToDelete)
        {
            issues.Add(Warning(IssueCodes.ProtectedPartition,
                $"This is a {partition.Type.Name} partition. Windows normally protects it, so Diskpart+ deletes it with \"override\".", operation));
        }

        if (partition.Type.Family == PartitionFamily.Apple)
        {
            issues.Add(Info(IssueCodes.ApplePartitions,
                "This is a Mac partition. Windows can't read it, so Diskpart+ can't show what's on it. Back up anything you need on a Mac first.", operation));
        }

        WarnAboutVolumes([partition], operation, issues);
    }

    private static void CheckPartitionMetadata(PartitionScopedOperation operation, PlanState state, List<PlanIssue> issues)
    {
        if (state.ResolvePartition(operation, null) is not { } partition || state.Snapshot.FindDisk(operation.DiskNumber) is not { } disk)
        {
            return;
        }

        if (partition.IsCritical)
        {
            issues.Add(Blocker(IssueCodes.CriticalPartition,
                $"{Describe(partition)} {WhyCritical(partition)}. Changing it could stop Windows starting.", operation));
        }
        else if (disk.IsCritical)
        {
            issues.Add(Warning(IssueCodes.OnWindowsDisk,
                "This partition is on the same disk as Windows. Double-check it's the one you mean.", operation));
        }
    }

    private static void CheckRemoveLetter(PartitionScopedOperation operation, PlanState state, List<PlanIssue> issues)
    {
        if (state.ResolvePartition(operation, null) is not { } partition)
        {
            return;
        }

        if (partition.IsBoot || partition.HoldsPagefile || partition.HostsApplication)
        {
            issues.Add(Blocker(IssueCodes.CriticalPartition,
                $"{Describe(partition)} {WhyCritical(partition)}. Its drive letter has to stay.", operation));
            return;
        }

        if (partition.Letter is { } letter)
        {
            issues.Add(Info(IssueCodes.LetterRemoval,
                $"Shortcuts and apps that use {letter.WithColon} will stop working until it has that letter again.", operation));
        }
    }

    private static void CheckDetach(DetachVirtualDisk detach, PlanState state, List<PlanIssue> issues)
    {
        if (state.Snapshot.FindVirtualDisk(detach.FilePath) is not { } disk)
        {
            return;
        }

        if (disk.IsCritical)
        {
            issues.Add(Blocker(IssueCodes.CriticalDisk, $"Disk {disk.Number} {WhyCritical(disk)}. It can't be detached.", detach));
            return;
        }

        var letters = disk.Partitions.Where(p => p.Letter is not null).Select(p => p.Letter!.Value.WithColon).ToList();
        if (letters.Count > 0)
        {
            issues.Add(Warning(IssueCodes.DetachInUse,
                $"{string.Join(", ", letters)} will disappear. Close anything using it first.", detach));
        }
    }

    private static DiskInfo? BlockIfCriticalDisk(DiskScopedOperation operation, PlanState state, List<PlanIssue> issues)
    {
        var disk = state.ResolveDisk(operation, null);
        if (disk is { IsCritical: true })
        {
            issues.Add(Blocker(IssueCodes.CriticalDisk, $"Disk {disk.Number} {WhyCritical(disk)}. Diskpart+ won't touch it.", operation));
            return null;
        }

        return disk;
    }

    private static void WarnAboutVolumes(IEnumerable<PartitionInfo> partitions, DiskOperation operation, List<PlanIssue> issues)
    {
        foreach (var partition in partitions)
        {
            if (partition.Volume is { IsBitLockerProtected: true })
            {
                issues.Add(Warning(IssueCodes.BitLocker,
                    $"{Describe(partition)} is protected by BitLocker. Make sure you don't need anything on it.", operation));
            }

            if (partition.Letter is { } letter)
            {
                issues.Add(Warning(IssueCodes.MountedVolume,
                    $"{letter.WithColon} is in use right now. Anything on it will be lost, including open files.", operation));
            }
        }
    }

    private static string WhyCritical(DiskInfo disk) => disk switch
    {
        { IsBoot: true } => "has your running copy of Windows on it",
        { IsSystem: true } => "has the files this PC boots from",
        { HoldsPagefile: true } => "has the Windows page file on it",
        _ => "is where Diskpart+ is running from",
    };

    private static string WhyCritical(PartitionInfo partition) => partition switch
    {
        { IsBoot: true } => "is your running copy of Windows",
        { IsSystem: true } => "holds the files this PC boots from",
        { HoldsPagefile: true } => "holds the Windows page file",
        _ => "is where Diskpart+ is running from",
    };

    private static string Describe(PartitionInfo partition) =>
        partition.Letter is { } letter
            ? letter.WithColon
            : $"Partition {partition.Number} on disk {partition.DiskNumber}";

    private static PlanIssue Blocker(string code, string message, DiskOperation operation) =>
        new(IssueSeverity.Blocker, code, message) { Operation = operation };

    private static PlanIssue Warning(string code, string message, DiskOperation operation) =>
        new(IssueSeverity.Warning, code, message) { Operation = operation };

    private static PlanIssue Info(string code, string message, DiskOperation operation) =>
        new(IssueSeverity.Info, code, message) { Operation = operation };
}
