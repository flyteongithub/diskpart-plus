using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Scripting;

namespace DiskpartPlus.Core.Execution;

/// <summary>Reads the current state of every disk. The Windows layer does this through the storage API.</summary>
public interface IStorageInventory
{
    Task<StorageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>Runs a script with "diskpart /s" and streams its output back line by line.</summary>
public interface IDiskpartRunner
{
    Task<DiskpartRunResult> RunAsync(
        DiskpartScript script,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// diskpart's exit codes when running a script. Anything other than zero means it stopped early,
/// and since we never use "noerr", nothing after the failing line ran.
/// </summary>
public enum DiskpartExitCode
{
    Unknown = -1,
    Success = 0,
    FatalError = 1,
    BadArguments = 2,
    ScriptFileError = 3,
    ServiceError = 4,
    CommandError = 5,
}

public sealed record DiskpartRunResult(int RawExitCode, IReadOnlyList<string> Output, TimeSpan Duration)
{
    public DiskpartExitCode ExitCode =>
        RawExitCode is >= 0 and <= 5 ? (DiskpartExitCode)RawExitCode : DiskpartExitCode.Unknown;

    public bool Succeeded => RawExitCode == 0;

    // We go by the exit code, not the text. diskpart's messages are translated into the
    // Windows display language, so matching on English words would break for most people.
    public string Explain() => ExitCode switch
    {
        DiskpartExitCode.Success => "Finished without errors.",
        DiskpartExitCode.FatalError => "diskpart hit a serious error and stopped.",
        DiskpartExitCode.BadArguments => "diskpart didn't accept how it was started.",
        DiskpartExitCode.ScriptFileError => "diskpart couldn't open the script file.",
        DiskpartExitCode.ServiceError => "A Windows disk service reported a failure. The output above has the details.",
        DiskpartExitCode.CommandError => "One of the commands failed. The output above shows which one.",
        _ => $"diskpart stopped with an unexpected code ({RawExitCode}).",
    };
}
