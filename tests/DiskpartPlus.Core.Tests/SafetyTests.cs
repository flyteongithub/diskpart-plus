using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Planning;
using DiskpartPlus.Core.Safety;

namespace DiskpartPlus.Core.Tests;

/// <summary>The rules that stop people wrecking the PC they're sitting at.</summary>
public class SafetyTests
{
    private static OperationPlan Plan(params DiskOperation[] operations) =>
        OperationPlanner.Create(operations, TestDisks.Context());

    private static bool Has(OperationPlan plan, string code, IssueSeverity severity) =>
        plan.Issues.Any(i => i.Code == code && i.Severity == severity);

    [Fact]
    public void Cleaning_the_windows_disk_is_blocked()
    {
        var plan = Plan(new CleanDisk(0));

        Assert.False(plan.CanRun);
        Assert.True(Has(plan, IssueCodes.CriticalDisk, IssueSeverity.Blocker));
    }

    [Fact]
    public void Formatting_c_is_blocked()
    {
        var plan = Plan(new FormatPartition(0, 3, new FormatSpec(FileSystemKind.Ntfs, "Nope")));

        Assert.True(Has(plan, IssueCodes.CriticalPartition, IssueSeverity.Blocker));
    }

    [Fact]
    public void Deleting_the_efi_partition_windows_boots_from_is_blocked()
    {
        var plan = Plan(new DeletePartition(0, 1, Override: true));

        Assert.True(Has(plan, IssueCodes.CriticalPartition, IssueSeverity.Blocker));
    }

    [Fact]
    public void Formatting_another_partition_on_the_windows_disk_is_allowed_without_fuss()
    {
        var plan = Plan(new FormatPartition(0, 5, new FormatSpec(FileSystemKind.Ntfs, "Data")));

        Assert.True(plan.CanRun);
        Assert.Empty(plan.Issues);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Deleting_mac_partitions_just_works(int partition)
    {
        var plan = Plan(new DeletePartition(1, partition, Override: true));

        Assert.True(plan.CanRun);
        Assert.Equal(RiskLevel.Destructive, plan.Risk);
        Assert.Empty(plan.Issues);
    }

    [Fact]
    public void Clean_all_on_an_ssd_warns()
    {
        var plan = Plan(new CleanDisk(4, ZeroEverySector: true));

        Assert.True(plan.CanRun);
        Assert.Equal(RiskLevel.Critical, plan.Risk);
        Assert.True(Has(plan, IssueCodes.CleanAllOnSsd, IssueSeverity.Warning));
    }

    [Fact]
    public void Clean_all_on_a_hard_drive_says_nothing()
    {
        Assert.Empty(Plan(new CleanDisk(3, ZeroEverySector: true)).Issues);
    }

    [Fact]
    public void Erasing_two_disks_in_one_plan_is_blocked()
    {
        var plan = Plan(new CleanDisk(1), new CleanDisk(2));

        Assert.True(Has(plan, IssueCodes.MultipleDisks, IssueSeverity.Blocker));
    }

    [Fact]
    public void Removing_the_letter_from_c_is_blocked()
    {
        var plan = Plan(new RemoveDriveLetter(0, 3, new DriveLetter('C')));

        Assert.True(Has(plan, IssueCodes.CriticalPartition, IssueSeverity.Blocker));
    }

    [Fact]
    public void Removing_a_normal_letter_is_fine()
    {
        var plan = Plan(new RemoveDriveLetter(3, 1, new DriveLetter('F')));

        Assert.True(plan.CanRun);
        Assert.Empty(plan.Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Taking_the_windows_disk_offline_or_read_only_is_blocked(bool readOnly)
    {
        DiskOperation operation = readOnly ? new SetDiskReadOnly(0, true) : new SetDiskOnline(0, false);

        Assert.True(Has(Plan(operation), IssueCodes.CriticalDisk, IssueSeverity.Blocker));
    }

    [Fact]
    public void Changing_the_windows_disk_id_is_blocked()
    {
        var plan = Plan(new SetDiskUniqueId(0, Guid.NewGuid().ToString()));

        Assert.True(Has(plan, IssueCodes.CriticalDisk, IssueSeverity.Blocker));
    }

    [Fact]
    public void Changing_the_type_of_the_boot_partition_is_blocked()
    {
        var plan = Plan(new SetPartitionType(0, 3, Partitions.PartitionTypes.WindowsRecovery));

        Assert.True(Has(plan, IssueCodes.CriticalPartition, IssueSeverity.Blocker));
    }

    [Fact]
    public void Clearing_read_only_is_fine()
    {
        var plan = Plan(new SetDiskReadOnly(2, false));

        Assert.True(plan.CanRun);
        Assert.Empty(plan.Issues);
    }

    [Fact]
    public void Risk_levels()
    {
        Assert.Equal(RiskLevel.Safe, SafetyPolicy.RiskOf(new AssignDriveLetter(3, 2)));
        Assert.Equal(RiskLevel.Caution, SafetyPolicy.RiskOf(new ShrinkPartition(3, 1, ByteSize.FromGigabytes(1))));
        Assert.Equal(RiskLevel.Destructive, SafetyPolicy.RiskOf(new FormatPartition(3, 1, new FormatSpec(FileSystemKind.Ntfs, ""))));
        Assert.Equal(RiskLevel.Critical, SafetyPolicy.RiskOf(new CleanDisk(1, ZeroEverySector: true)));
    }
}
