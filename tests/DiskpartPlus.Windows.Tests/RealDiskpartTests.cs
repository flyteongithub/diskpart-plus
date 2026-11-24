using System.Runtime.Versioning;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Planning;
using DiskpartPlus.Core.Scripting;
using DiskpartPlus.Core.Templates;
using DiskpartPlus.Windows;
using DiskpartPlus.Windows.Execution;
using DiskpartPlus.Windows.Inventory;
using Xunit.Abstractions;

namespace DiskpartPlus.Windows.Tests;

/// <summary>
/// These run real diskpart against a throwaway VHDX in the temp folder. They never touch a real disk.
/// They only run on Windows, as administrator, with DISKPARTPLUS_INTEGRATION=1 set.
/// CI sets that. On your own PC they quietly do nothing unless you set it too.
/// </summary>
[SupportedOSPlatform("windows")]
public class RealDiskpartTests(ITestOutputHelper log)
{
    private readonly CimStorageInventory _inventory = new();
    private readonly DiskpartRunner _runner = new();

    private static bool Enabled =>
        OperatingSystem.IsWindows()
        && Environment.GetEnvironmentVariable("DISKPARTPLUS_INTEGRATION") == "1"
        && Elevation.IsAdministrator();

    [Fact]
    public async Task Inventory_finds_the_windows_disk_and_planner_protects_it()
    {
        if (!Enabled)
        {
            return;
        }

        var snapshot = await _inventory.GetSnapshotAsync();
        foreach (var disk in snapshot.Disks)
        {
            log.WriteLine($"{disk.DisplayName} | {disk.Style} | {disk.Bus} | {disk.Media} | system={disk.IsSystem} boot={disk.IsBoot} pagefile={disk.HoldsPagefile} app={disk.HostsApplication} | {disk.Location}");
            foreach (var p in disk.Partitions)
            {
                log.WriteLine($"    #{p.Number} {p.Letter?.WithColon ?? "  "} {p.Type.Name} | {p.FileSystem.DisplayName()} \"{p.Volume?.Label}\" | {p.Size} at {p.Offset} | system={p.IsSystem} boot={p.IsBoot} pagefile={p.HoldsPagefile}");
            }
        }

        var windows = Assert.Single(snapshot.Disks, d => d.IsBoot);
        Assert.Contains(windows.Partitions, p => p.IsBoot && p.Letter == new DriveLetter('C'));
        Assert.True(windows.IsCritical);

        var plan = OperationPlanner.Create(WipeAndReuse.Build(windows.Number), new PlanningContext(snapshot));
        Assert.False(plan.CanRun);
    }

    [Fact]
    public async Task A_failing_script_returns_a_non_zero_exit_code()
    {
        if (!Enabled)
        {
            return;
        }

        var result = await _runner.RunAsync(new DiskpartScript(["select disk 987"]), new LineLog(log.WriteLine));
        log.WriteLine($"Exit code: {result.RawExitCode} ({result.ExitCode})");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Every_operation_works_on_a_throwaway_virtual_disk()
    {
        if (!Enabled)
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"diskpartplus-test-{Guid.NewGuid():N}.vhdx");
        log.WriteLine($"Using {path}");

        try
        {
            // 1. Create, attach and set up the disk in one go.
            await Run(new CreateVirtualDisk(
                path,
                ByteSize.FromMegabytes(512),
                Setup: new VirtualDiskSetup(PartitionStyle.Gpt, new FormatSpec(FileSystemKind.Ntfs, "DPTEST"), DriveAssignment.NextFree)));

            var disk = await VirtualDisk(path);
            Assert.Equal(PartitionStyle.Gpt, disk.Style);
            var data = Assert.Single(disk.Partitions, p => p.Type == PartitionTypes.BasicData);
            Assert.Equal(FileSystemKind.Ntfs, data.FileSystem);
            Assert.Equal("DPTEST", data.Volume?.Label);
            Assert.NotNull(data.Letter);

            // 2. Wipe and reuse.
            await Run([.. WipeAndReuse.Build(disk.Number, new WipeAndReuseOptions { Label = "REUSED" })]);
            disk = await VirtualDisk(path);
            data = Assert.Single(disk.Partitions, p => p.Type == PartitionTypes.BasicData);
            Assert.Equal(FileSystemKind.ExFat, data.FileSystem);
            Assert.Equal("REUSED", data.Volume?.Label);

            // 3. Format it back to NTFS.
            await Run(new FormatPartition(disk.Number, data.Number, new FormatSpec(FileSystemKind.Ntfs, "AGAIN")));
            disk = await VirtualDisk(path);
            data = disk.FindPartition(data.Number)!;
            Assert.Equal(FileSystemKind.Ntfs, data.FileSystem);

            // 4. Shrink it and change its letter.
            var sizeBefore = data.Size;
            var newLetter = FreeLetter(await _inventory.GetSnapshotAsync());
            await Run(
                new ShrinkPartition(disk.Number, data.Number, ByteSize.FromMegabytes(64)),
                new AssignDriveLetter(disk.Number, data.Number, newLetter, Replacing: data.Letter));
            disk = await VirtualDisk(path);
            data = disk.FindPartition(data.Number)!;
            Assert.Equal(sizeBefore - ByteSize.FromMegabytes(64), data.Size);
            Assert.Equal(newLetter, data.Letter);

            // 5. Grow it back, take the letter away and set some GPT attributes.
            await Run(
                new ExtendPartition(disk.Number, data.Number),
                new RemoveDriveLetter(disk.Number, data.Number, newLetter),
                new SetGptAttributes(disk.Number, data.Number, GptAttributeFlags.NoDriveLetter));
            disk = await VirtualDisk(path);
            data = disk.FindPartition(data.Number)!;
            Assert.Equal(sizeBefore, data.Size);
            Assert.Null(data.Letter);
            Assert.True(data.GptAttributes.HasFlag(GptAttributeFlags.NoDriveLetter));

            // 6. Change its type, then delete it (needs override once it isn't basic data any more).
            await Run(new SetPartitionType(disk.Number, data.Number, PartitionTypes.LinuxFilesystem));
            disk = await VirtualDisk(path);
            Assert.Equal(PartitionTypes.LinuxFilesystem, disk.FindPartition(data.Number)!.Type);

            await Run(new DeletePartition(disk.Number, data.Number, Override: true));
            disk = await VirtualDisk(path);
            Assert.DoesNotContain(disk.Partitions, p => p.Number == data.Number);

            // 7. Start again as MBR with a small FAT32 partition and a new disk ID.
            await Run(
                new CleanDisk(disk.Number),
                new ConvertPartitionStyle(disk.Number, PartitionStyle.Mbr),
                new CreatePartition(disk.Number, Size: ByteSize.FromMegabytes(128), Format: new FormatSpec(FileSystemKind.Fat32, "SMALL"), Assign: DriveAssignment.NextFree),
                new SetDiskUniqueId(disk.Number, "1A2B3C4D"));
            disk = await VirtualDisk(path);
            Assert.Equal(PartitionStyle.Mbr, disk.Style);
            Assert.Contains(disk.Partitions, p => p.FileSystem == FileSystemKind.Fat32 && p.Volume?.Label == "SMALL");

            // 8. Read-only on and off, offline and back online.
            await Run(new SetDiskReadOnly(disk.Number, true), new SetDiskReadOnly(disk.Number, false));
            await Run(new SetDiskOnline(disk.Number, false), new SetDiskOnline(disk.Number, true));
            disk = await VirtualDisk(path);
            Assert.False(disk.IsReadOnly);
            Assert.False(disk.IsOffline);

            // 9. Detach it.
            await Run(new DetachVirtualDisk(path));
            Assert.Null((await _inventory.GetSnapshotAsync()).FindVirtualDisk(path));

            // 10. Attach it again read-only, then detach.
            var context = new PlanningContext(await _inventory.GetSnapshotAsync()) { FileExists = File.Exists };
            await Run(context, new AttachVirtualDisk(path, ReadOnly: true));
            Assert.NotNull((await _inventory.GetSnapshotAsync()).FindVirtualDisk(path));
            await Run(new DetachVirtualDisk(path));
        }
        finally
        {
            await CleanUp(path);
        }
    }

    private async Task Run(params DiskOperation[] operations)
    {
        var context = new PlanningContext(await _inventory.GetSnapshotAsync()) { FileExists = File.Exists };
        await Run(context, operations);
    }

    private async Task Run(PlanningContext context, params DiskOperation[] operations)
    {
        var plan = OperationPlanner.Create(operations, context);
        foreach (var issue in plan.Issues)
        {
            log.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
        }

        Assert.True(plan.CanRun, "The plan was blocked. See the issues above.");

        var problems = plan.CheckTargetsStillMatch(await _inventory.GetSnapshotAsync());
        Assert.Empty(problems);

        log.WriteLine(plan.Script!.ToText());
        var result = await _runner.RunAsync(plan.Script, new LineLog(line => log.WriteLine("  > " + line)));
        log.WriteLine($"Exit code {result.CodeText} after {result.Duration.TotalSeconds:0.0}s");

        Assert.True(result.Succeeded, $"diskpart failed: {result.Explain()}");
    }

    private async Task<DiskInfo> VirtualDisk(string path)
    {
        var disk = (await _inventory.GetSnapshotAsync()).FindVirtualDisk(path);
        Assert.NotNull(disk);
        log.WriteLine($"Now: {disk.DisplayName} {disk.Style}, partitions: {string.Join(", ", disk.Partitions.Select(p => $"#{p.Number} {p.Type.Name} {p.FileSystem.DisplayName()} \"{p.Volume?.Label}\" {p.Letter?.WithColon} {p.Size}"))}");
        return disk;
    }

    private static DriveLetter FreeLetter(StorageSnapshot snapshot) =>
        snapshot.AvailableLetters().Last();

    private async Task CleanUp(string path)
    {
        try
        {
            if ((await _inventory.GetSnapshotAsync()).FindVirtualDisk(path) is not null)
            {
                await _runner.RunAsync(DiskpartScriptBuilder.Build([new DetachVirtualDisk(path)]));
            }
        }
        catch (Exception ex)
        {
            log.WriteLine($"Clean up couldn't detach: {ex.Message}");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>Reports straight away on the calling thread, so nothing gets logged after a test has finished.</summary>
internal sealed class LineLog(Action<string> write) : IProgress<string>
{
    public void Report(string value) => write(value);
}
