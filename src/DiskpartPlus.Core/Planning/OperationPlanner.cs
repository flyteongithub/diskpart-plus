using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Safety;
using DiskpartPlus.Core.Scripting;

namespace DiskpartPlus.Core.Planning;

/// <summary>
/// The one way to turn operations into something runnable. It checks every step against the
/// snapshot (and against the steps before it), works out the risk and builds the script.
/// </summary>
public static class OperationPlanner
{
    public static OperationPlan Create(IEnumerable<DiskOperation> operations, PlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(context);

        var list = operations.ToList();
        var issues = new List<PlanIssue>();
        var state = new PlanState(context);

        if (list.Count == 0)
        {
            issues.Add(new PlanIssue(IssueSeverity.Blocker, IssueCodes.EmptyPlan, "Nothing to do."));
        }

        foreach (var operation in list)
        {
            OperationValidator.Check(operation, state, issues);
            SafetyPolicy.Check(operation, state, issues);
            state.Apply(operation);
        }

        SafetyPolicy.CheckWholePlan(list, issues);

        // Build the script even when there are blockers, so the user can still see what would have run.
        DiskpartScript? script;
        try
        {
            script = DiskpartScriptBuilder.Build(list);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // The validator should catch bad input first. If it didn't, refuse rather than guess.
            script = null;
            issues.Add(new PlanIssue(IssueSeverity.Blocker, IssueCodes.ScriptError, ex.Message));
        }

        var targets = list
            .OfType<DiskScopedOperation>()
            .Select(o => o.DiskNumber)
            .Distinct()
            .Select(context.Snapshot.FindDisk)
            .OfType<DiskInfo>()
            .Select(DiskFingerprint.From)
            .ToList();

        return new OperationPlan(list, script, Dedupe(issues), targets);
    }

    // The same message can come up twice, for example when two steps touch the same volume.
    private static List<PlanIssue> Dedupe(List<PlanIssue> issues) =>
        issues
            .GroupBy(i => (i.Severity, i.Code, i.Message))
            .Select(g => g.First())
            .OrderByDescending(i => i.Severity)
            .ToList();
}
