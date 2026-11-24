using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using DiskpartPlus.Core.Execution;
using DiskpartPlus.Core.Scripting;

namespace DiskpartPlus.Windows.Execution;

/// <summary>
/// Runs a script with "diskpart /s" and streams what it prints, line by line.
/// </summary>
/// <remarks>
/// Only one script runs at a time. Microsoft's docs say to leave about 15 seconds between
/// back-to-back scripts so the last diskpart has fully shut down, so we wait if we need to.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DiskpartRunner : IDiskpartRunner
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private static DateTimeOffset _lastFinished = DateTimeOffset.MinValue;

    static DiskpartRunner()
    {
        // diskpart writes in the console's OEM code page, which .NET doesn't load by default.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(15);

    public async Task<DiskpartRunResult> RunAsync(
        DiskpartScript script,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);

        await OneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        var folder = Path.Combine(Path.GetTempPath(), "DiskpartPlus", Guid.NewGuid().ToString("N"));

        try
        {
            var wait = _lastFinished + Cooldown - DateTimeOffset.Now;
            if (wait > TimeSpan.Zero)
            {
                output?.Report($"Waiting {Math.Ceiling(wait.TotalSeconds):0} seconds for the last diskpart run to close...");
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            Directory.CreateDirectory(folder);
            var scriptPath = Path.Combine(folder, "script.txt");
            await File.WriteAllTextAsync(scriptPath, script.ToText(), ScriptEncodingFor(script), cancellationToken).ConfigureAwait(false);

            return await RunDiskpartAsync(scriptPath, output, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lastFinished = DateTimeOffset.Now;
            TryDelete(folder);
            OneAtATime.Release();
        }
    }

    private static async Task<DiskpartRunResult> RunDiskpartAsync(string scriptPath, IProgress<string>? output, CancellationToken cancellationToken)
    {
        var console = OemEncoding();
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "diskpart.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = console,
            StandardErrorEncoding = console,
        };
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add(scriptPath);

        var lines = new List<string>();
        void OnLine(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (lines)
            {
                lines.Add(line);
            }

            output?.Report(line);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        var timer = Stopwatch.StartNew();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Only "clean all" offers a stop button, and stopping that just leaves part of the disk zeroed.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            throw;
        }

        // Make sure the last lines have been read before we hand the result back.
        process.WaitForExit();
        timer.Stop();

        List<string> snapshot;
        lock (lines)
        {
            snapshot = [.. lines];
        }

        return new DiskpartRunResult(process.ExitCode, snapshot, timer.Elapsed);
    }

    // Plain ASCII when we can. If a label has accents or other non-ASCII characters,
    // write UTF-16 with a byte order mark so diskpart reads them correctly.
    private static Encoding ScriptEncodingFor(DiskpartScript script) =>
        script.Lines.All(l => l.All(char.IsAscii)) ? Encoding.ASCII : new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

    private static Encoding OemEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Default;
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // Not worth failing over. It's an empty-ish temp folder.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
