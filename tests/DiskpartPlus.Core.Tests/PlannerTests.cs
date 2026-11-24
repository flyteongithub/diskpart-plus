using DiskpartPlus.Core.Execution;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Planning;
using DiskpartPlus.Core.Safety;
using DiskpartPlus.Core.Templates;

namespace DiskpartPlus.Core.Tests;

public class PlannerTests
{
    [Fact]
    public void Safe_plans_run_without_a_review()
    {
        var plan = OperationPlanner.Create([new AssignDriveLetter(3, 2)], TestDisks.Context());

        Assert.True(plan.CanRun);
        Assert.Equal(RiskLevel.Safe, plan.Risk);
        Assert.False(plan.NeedsReview);
        Assert.Null(plan.ErasedDisk);
    }

    [Fact]
    public void Destructive_plans_get_a_review_naming_the_disk()
    {
        var plan = OperationPlanner.Create(WipeAndReuse.Build(1), TestDisks.Context());

        Assert.True(plan.NeedsReview);
        Assert.Equal(1, plan.ErasedDisk);
    }

    [Fact]
    public void Blocked_plans_still_show_the_script()
    {
        var plan = OperationPlanner.Create([new CleanDisk(0)], TestDisks.Context());

        Assert.False(plan.CanRun);
        Assert.NotNull(plan.Script);
        Assert.Contains("clean", plan.Script!.Commands);
    }

    [Fact]
    public void Blockers_come_first()
    {
        var plan = OperationPlanner.Create([new CleanDisk(0), new FormatPartition(0, 5, new FormatSpec(FileSystemKind.Ntfs, "Data"))], TestDisks.Context());

        Assert.Equal(IssueSeverity.Blocker, plan.Issues[0].Severity);
    }

    [Fact]
    public void Targets_are_recorded()
    {
        var plan = OperationPlanner.Create(WipeAndReuse.Build(1), TestDisks.Context());

        var target = Assert.Single(plan.Targets);
        Assert.Equal(1, target.Number);
        Assert.Equal(TestDisks.MacDrive.UniqueId, target.UniqueId);
    }

    [Fact]
    public void Same_disks_pass_the_last_minute_check()
    {
        var plan = OperationPlanner.Create(WipeAndReuse.Build(1), TestDisks.Context());

        Assert.Empty(plan.CheckTargetsStillMatch(TestDisks.Snapshot()));
    }

    [Fact]
    public void A_different_drive_under_the_same_number_fails_the_last_minute_check()
    {
        var plan = OperationPlanner.Create(WipeAndReuse.Build(1), TestDisks.Context());

        // Someone unplugged the Mac drive and plugged in the SSD, which Windows then called disk 1.
        var imposter = TestDisks.ExternalSsd with { Number = 1 };
        var problems = plan.CheckTargetsStillMatch(TestDisks.Snapshot(TestDisks.WindowsDisk, imposter));

        var problem = Assert.Single(problems);
        Assert.Contains("Samsung PSSD T7", problem);
    }

    [Fact]
    public void An_unplugged_drive_fails_the_last_minute_check()
    {
        var plan = OperationPlanner.Create(WipeAndReuse.Build(1), TestDisks.Context());

        var problems = plan.CheckTargetsStillMatch(TestDisks.Snapshot(TestDisks.WindowsDisk));

        Assert.Contains("is gone", Assert.Single(problems));
    }

    [Fact]
    public void Fingerprints_fall_back_to_serial_numbers()
    {
        var noId = TestDisks.UsbStick with { UniqueId = null };
        var fingerprint = DiskFingerprint.From(noId);

        Assert.True(fingerprint.Matches(noId));
        Assert.False(fingerprint.Matches(noId with { SerialNumber = "SOMETHINGELSE" }));
    }

    [Fact]
    public void Wipe_and_reuse_on_a_mac_drive()
    {
        var plan = OperationPlanner.Create(WipeAndReuse.Build(1), TestDisks.Context());

        Assert.True(plan.CanRun);
        Assert.Equal(
            new[]
            {
                "select disk 1",
                "clean",
                "select disk 1",
                "convert gpt",
                "select disk 1",
                "create partition primary",
                "format fs=exfat label=\"DATA\" quick",
                "assign",
            },
            plan.Script!.Commands.ToList());
    }

    [Fact]
    public void Wipe_and_reuse_as_mbr()
    {
        var plan = OperationPlanner.Create(WipeAndReuse.Build(2, new WipeAndReuseOptions { Style = PartitionStyle.Mbr }), TestDisks.Context());

        Assert.True(plan.CanRun);
        Assert.Contains("convert mbr", plan.Script!.Commands);
    }

    [Fact]
    public void Wipe_and_reuse_wont_touch_the_windows_disk()
    {
        var plan = OperationPlanner.Create(WipeAndReuse.Build(0), TestDisks.Context());

        Assert.False(plan.CanRun);
    }

    [Fact]
    public void Wipe_and_reuse_catches_fat32_on_a_big_drive()
    {
        var options = new WipeAndReuseOptions { FileSystem = FileSystemKind.Fat32, Label = "MAC" };
        var plan = OperationPlanner.Create(WipeAndReuse.Build(1, options), TestDisks.Context());

        Assert.Contains(plan.Issues, i => i.Code == IssueCodes.Fat32TooLarge && i.Severity == IssueSeverity.Blocker);
    }

    [Fact]
    public void Exit_codes_are_explained_and_hresults_shown_in_hex()
    {
        var hresult = new DiskpartRunResult(-2147024809, [], TimeSpan.Zero);

        Assert.False(hresult.Succeeded);
        Assert.Equal("0x80070057", hresult.CodeText);
        Assert.Contains("0x80070057", hresult.Explain());
        Assert.True(new DiskpartRunResult(0, [], TimeSpan.Zero).Succeeded);
    }
}
