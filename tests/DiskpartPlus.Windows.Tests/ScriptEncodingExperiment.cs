using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Planning;
using DiskpartPlus.Windows.Execution;
using DiskpartPlus.Windows.Inventory;
using Xunit.Abstractions;

namespace DiskpartPlus.Windows.Tests;

/// <summary>
/// Temporary: works out which text encoding diskpart reads script files in, by formatting a
/// throwaway VHDX with an accented label in each encoding and reading the label back.
/// </summary>
[SupportedOSPlatform("windows")]
public class ScriptEncodingExperiment(ITestOutputHelper log)
{
    private const string Label = "ÄÖÜ-éß";

    [Fact]
    public async Task Try_each_encoding()
    {
        if (Environment.GetEnvironmentVariable("DISKPARTPLUS_INTEGRATION") != "1" || !Elevation.IsAdministrator())
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = CultureInfo.CurrentCulture.TextInfo;
        log.WriteLine($"ANSI code page {text.ANSICodePage}, OEM code page {text.OEMCodePage}, console {Console.OutputEncoding.CodePage}");

        var inventory = new CimStorageInventory();
        var runner = new DiskpartRunner();
        var path = Path.Combine(Path.GetTempPath(), $"diskpartplus-enc-{Guid.NewGuid():N}.vhdx");

        try
        {
            var setup = new CreateVirtualDisk(path, ByteSize.FromMegabytes(256), Setup: new VirtualDiskSetup(PartitionStyle.Gpt, new FormatSpec(FileSystemKind.Ntfs, "START")));
            var plan = OperationPlanner.Create([setup], new PlanningContext(await inventory.GetSnapshotAsync()));
            var made = await runner.RunAsync(plan.Script!);
            log.WriteLine($"Setup exit {made.CodeText}");

            var disk = (await inventory.GetSnapshotAsync()).FindVirtualDisk(path)!;
            var partition = disk.Partitions.First(p => p.Type.IsDataPartition).Number;

            var candidates = new (string Name, Encoding Encoding)[]
            {
                ("OEM", Encoding.GetEncoding(text.OEMCodePage)),
                ("ANSI", Encoding.GetEncoding(text.ANSICodePage)),
                ("UTF-8 with BOM", new UTF8Encoding(true)),
                ("UTF-8 no BOM", new UTF8Encoding(false)),
            };

            foreach (var (name, encoding) in candidates)
            {
                await Task.Delay(TimeSpan.FromSeconds(16));
                var script = Path.Combine(Path.GetTempPath(), $"enc-{Guid.NewGuid():N}.txt");
                await File.WriteAllTextAsync(script, $"select disk {disk.Number}\r\nselect partition {partition}\r\nformat fs=ntfs label=\"{Label}\" quick override\r\n", encoding);

                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "diskpart.exe")) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                start.ArgumentList.Add("/s");
                start.ArgumentList.Add(script);
                using var process = Process.Start(start)!;
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                File.Delete(script);

                var label = (await inventory.GetSnapshotAsync()).FindVirtualDisk(path)?.FindPartition(partition)?.Volume?.Label;
                var verdict = label == Label ? "MATCH" : "no match";
                log.WriteLine($"RESULT {name}: exit {process.ExitCode}, label read back \"{label}\" -> {verdict}");
                if (process.ExitCode != 0)
                {
                    log.WriteLine(string.Join(" | ", output.Split('\n').Where(l => l.Trim().Length > 0).Take(8)));
                }
            }
        }
        finally
        {
            await Task.Delay(TimeSpan.FromSeconds(16));
            await runner.RunAsync(new Core.Scripting.DiskpartScript([$"select vdisk file=\"{path}\"", "detach vdisk"]));
            File.Delete(path);
        }
    }
}
