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
/// The small exit codes Microsoft documents for diskpart scripts. On current Windows, a failing
/// command usually returns a Windows error code (an HRESULT like 0x80070057) instead, so treat
/// anything that isn't zero as a failure and show the code.
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

    /// <summary>The exit code the way Windows people expect to see it, like 0x80070057.</summary>
    public string CodeText => RawExitCode < 0 ? $"0x{(uint)RawExitCode:X8}" : RawExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
        _ => $"diskpart stopped with error {CodeText}. The output above shows which command failed.",
    };
}
