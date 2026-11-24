using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Safety;
using DiskpartPlus.Core.Scripting;

namespace DiskpartPlus.Core.Planning;

/// <summary>
/// A checked, ready-to-show set of operations: what will happen, how risky it is,
/// anything the user should know and the exact diskpart script that will run.
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
        ConfirmationPhrase = BuildConfirmationPhrase(operations, Risk);
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

    /// <summary>
    /// What the user has to type before a destructive plan runs, like "ERASE DISK 2".
    /// Null when the plan doesn't need it.
    /// </summary>
    public string? ConfirmationPhrase { get; }

    public bool RequiresConfirmation => ConfirmationPhrase is not null;

    public bool IsConfirmedBy(string? typed)
    {
        if (ConfirmationPhrase is null)
        {
            return true;
        }

        // Be forgiving about case and spacing, strict about the words and the disk number.
        var cleaned = string.Join(' ', (typed ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.Equals(cleaned, ConfirmationPhrase, StringComparison.OrdinalIgnoreCase);
    }

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
                problems.Add($"Disk {target.Number} ({target.FriendlyName}) has disappeared. Was it unplugged?");
            }
            else if (!target.Matches(disk))
            {
                problems.Add($"Disk {target.Number} is now {disk.FriendlyName} ({disk.Size}), not {target.FriendlyName} ({target.Size}). Refresh and check again.");
            }
        }

        return problems;
    }

    private static string? BuildConfirmationPhrase(IReadOnlyList<DiskOperation> operations, RiskLevel risk)
    {
        if (risk < RiskLevel.Destructive)
        {
            return null;
        }

        var disk = operations
            .Where(o => SafetyPolicy.RiskOf(o) >= RiskLevel.Destructive)
            .OfType<DiskScopedOperation>()
            .Select(o => (int?)o.DiskNumber)
            .FirstOrDefault();

        if (disk is null)
        {
            return null;
        }

        return risk == RiskLevel.Critical ? $"WIPE DISK {disk}" : $"ERASE DISK {disk}";
    }
}
