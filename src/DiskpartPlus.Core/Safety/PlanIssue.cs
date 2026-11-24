using DiskpartPlus.Core.Operations;

namespace DiskpartPlus.Core.Safety;

public enum RiskLevel
{
    /// <summary>Nothing gets lost. Giving a partition a drive letter, attaching a VHD.</summary>
    Safe,

    /// <summary>Changes things but keeps your data, like shrinking or taking a disk offline.</summary>
    Caution,

    /// <summary>Data is gone afterwards. Formatting, deleting, cleaning.</summary>
    Destructive,

    /// <summary>Data is gone and it takes a long time. Only "clean all" is up here.</summary>
    Critical,
}

public enum IssueSeverity
{
    Info,
    Warning,

    /// <summary>The plan can't run until this is fixed.</summary>
    Blocker,
}

/// <summary>A reason a plan can't run. Occasionally just a warning worth knowing.</summary>
public sealed record PlanIssue(IssueSeverity Severity, string Code, string Message)
{
    /// <summary>The operation this is about, if it's about one in particular.</summary>
    public DiskOperation? Operation { get; init; }

    public override string ToString() => $"{Severity}: {Message}";
}

/// <summary>
/// Stable codes for every issue, so tests and translations can rely on them
/// instead of matching message text.
/// </summary>
public static class IssueCodes
{
    // Something referenced doesn't exist or doesn't fit
    public const string DiskNotFound = "disk-not-found";
    public const string PartitionNotFound = "partition-not-found";
    public const string PartitionGone = "partition-gone";
    public const string DiskOffline = "disk-offline";
    public const string DiskReadOnly = "disk-read-only";
    public const string DiskNotEmpty = "disk-not-empty";
    public const string AlreadyThatStyle = "already-that-style";
    public const string InvalidStyle = "invalid-style";
    public const string GptOnly = "gpt-only";
    public const string NotEnoughSpace = "not-enough-space";
    public const string NoSpaceAfter = "no-space-after";
    public const string TooSmall = "too-small";
    public const string UnsupportedFileSystem = "unsupported-file-system";
    public const string CantFormatTo = "cant-format-to";
    public const string LabelTooLong = "label-too-long";
    public const string LabelBadCharacter = "label-bad-character";
    public const string UnsupportedCharacters = "unsupported-characters";
    public const string Fat32TooLarge = "fat32-too-large";
    public const string LetterInUse = "letter-in-use";
    public const string AlreadyHasLetter = "already-has-letter";
    public const string LetterMismatch = "letter-mismatch";
    public const string TypeDoesNotFitDisk = "type-does-not-fit-disk";
    public const string InvalidDiskId = "invalid-disk-id";
    public const string InvalidPath = "invalid-path";
    public const string FileExists = "file-exists";
    public const string FileMissing = "file-missing";
    public const string VirtualDiskTooBig = "virtual-disk-too-big";
    public const string AlreadyAttached = "already-attached";
    public const string NotAttached = "not-attached";
    public const string EmptyPlan = "empty-plan";
    public const string ScriptError = "script-error";

    // Safety
    public const string CriticalDisk = "critical-disk";
    public const string CriticalPartition = "critical-partition";
    public const string CleanAllOnSsd = "clean-all-on-ssd";
    public const string MultipleDisks = "multiple-disks";
}
