using DiskpartPlus.Core.Models;

namespace DiskpartPlus.Core.Planning;

/// <summary>What the planner needs to know about the machine.</summary>
public sealed record PlanningContext(StorageSnapshot Snapshot)
{
    /// <summary>
    /// Lets the planner ask whether a file exists (for VHD paths) without Core touching the disk itself.
    /// The Windows layer passes File.Exists. Tests pass whatever they like.
    /// </summary>
    public Func<string, bool> FileExists { get; init; } = static _ => false;

    /// <summary>
    /// Whether a piece of text (a label or a path) can go into a diskpart script on this PC.
    /// diskpart reads scripts in the console's code page, which can't hold every character.
    /// </summary>
    public Func<string, bool> ScriptCanHold { get; init; } = static _ => true;
}
