using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Safety;
using DiskpartPlus.Core.Scripting;

namespace DiskpartPlus.Core.Planning;

/// <summary>
/// A checked set of operations: what will happen, how risky it is, anything blocking it
/// and the exact diskpart script that will run.
/// Make one with <see cref="OperationPlanner.Create"/>.
/// </summary>
public sealed class OperationPlan
{
    internal OperationPlan(
        IReadOnlyList<DiskOperation> operations,
        DiskpartScript? script,
        IReadOnlyList<PlanIssue> issues,
        IReadOnlyList<DiskFingerprint> targets)
    {
        Operations = operations;
        Script = script;
        Issues = issues;
        Targets = targets;
        Risk = operations.Count == 0 ? RiskLevel.Safe : operations.Max(SafetyPolicy.RiskOf);
    }

    public IReadOnlyList<DiskOperation> Operations { get; }

    /// <summary>The script that will run. Null only if an operation couldn't be turned into a script at all.</summary>
    public DiskpartScript? Script { get; }

    public IReadOnlyList<PlanIssue> Issues { get; }

    /// <summary>The disks this plan touches, as they were when it was made.</summary>
    public IReadOnlyList<DiskFingerprint> Targets { get; }

    public RiskLevel Risk { get; }

    public bool HasBlockers => Issues.Any(i => i.Severity == IssueSeverity.Blocker);

    public bool CanRun => !HasBlockers && Script is not null && Operations.Count > 0;

    /// <summary>True for anything that erases data. The app shows these once before running.</summary>
    public bool NeedsReview => Risk >= RiskLevel.Destructive;

    /// <summary>The disk a destructive plan erases, for labels like "Erase disk 2".</summary>
    public int? ErasedDisk => Operations
        .Where(o => SafetyPolicy.RiskOf(o) >= RiskLevel.Destructive)
        .OfType<DiskScopedOperation>()
        .Select(o => (int?)o.DiskNumber)
        .FirstOrDefault();

    /// <summary>
    /// Checks the disks are still the same physical drives. Returns a list of problems,
    /// or an empty list if it's safe to go ahead.
    /// </summary>
    public IReadOnlyList<string> CheckTargetsStillMatch(StorageSnapshot fresh)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        var problems = new List<string>();

        foreach (var target in Targets)
        {
            var disk = fresh.FindDisk(target.Number);
            if (disk is null)
            {
                problems.Add($"Disk {target.Number} ({target.FriendlyName}) is gone.");
            }
            else if (!target.Matches(disk))
            {
                problems.Add($"Disk {target.Number} is now {disk.FriendlyName} ({disk.Size}), not {target.FriendlyName}. Refresh.");
            }
        }

        return problems;
    }
}
