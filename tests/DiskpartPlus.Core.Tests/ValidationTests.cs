using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Planning;
using DiskpartPlus.Core.Safety;
using DiskpartPlus.Core.Templates;

namespace DiskpartPlus.Core.Tests;

/// <summary>Things diskpart would reject, caught before anything runs.</summary>
public class ValidationTests
{
    private static OperationPlan Plan(params DiskOperation[] operations) =>
        OperationPlanner.Create(operations, TestDisks.Context());

    private static bool Blocked(OperationPlan plan, string code) =>
        plan.Issues.Any(i => i.Code == code && i.Severity == IssueSeverity.Blocker);

    [Fact]
    public void Unknown_disk()
    {
        Assert.True(Blocked(Plan(new CleanDisk(9)), IssueCodes.DiskNotFound));
    }

    [Fact]
    public void Unknown_partition()
    {
        Assert.True(Blocked(Plan(new DeletePartition(3, 7)), IssueCodes.PartitionNotFound));
    }

    [Fact]
    public void Empty_plan()
    {
        Assert.True(Blocked(Plan(), IssueCodes.EmptyPlan));
    }

    [Fact]
    public void Converting_a_disk_with_partitions_is_blocked()
    {
        Assert.True(Blocked(Plan(new ConvertPartitionStyle(2, PartitionStyle.Gpt)), IssueCodes.DiskNotEmpty));
    }

    [Fact]
    public void Converting_after_clean_is_fine()
    {
        var plan = Plan(new CleanDisk(2), new ConvertPartitionStyle(2, PartitionStyle.Gpt));

        Assert.True(plan.CanRun);
    }

    [Fact]
    public void Converting_after_deleting_every_partition_is_fine()
    {
        var plan = Plan(new DeletePartition(2, 1), new ConvertPartitionStyle(2, PartitionStyle.Gpt));

        Assert.True(plan.CanRun);
    }

    [Fact]
    public void Partitions_are_gone_after_clean()
    {
        var plan = Plan(new CleanDisk(3), new FormatPartition(3, 1, new FormatSpec(FileSystemKind.Ntfs, "Games")));

        Assert.True(Blocked(plan, IssueCodes.PartitionGone));
    }

    [Fact]
    public void Fat32_over_32gb_is_blocked()
    {
        var plan = Plan(new FormatPartition(3, 1, new FormatSpec(FileSystemKind.Fat32, "GAMES")));

        Assert.True(Blocked(plan, IssueCodes.Fat32TooLarge));
    }

    [Fact]
    public void Fat32_labels_are_short()
    {
        var plan = Plan(new FormatPartition(2, 1, new FormatSpec(FileSystemKind.Fat32, "THIS IS TOO LONG")));

        Assert.True(Blocked(plan, IssueCodes.LabelTooLong));
    }

    [Fact]
    public void Labels_with_quotes_are_blocked_before_the_script_is_built()
    {
        var plan = Plan(new FormatPartition(3, 1, new FormatSpec(FileSystemKind.Ntfs, "say \"hi\"")));

        Assert.True(Blocked(plan, IssueCodes.LabelBadCharacter));
        Assert.False(plan.CanRun);
    }

    [Fact]
    public void Letters_already_in_use_are_blocked()
    {
        var plan = Plan(new CreatePartition(3, Size: ByteSize.FromGigabytes(10), Assign: new DriveAssignment(new DriveLetter('C'))));

        Assert.True(Blocked(plan, IssueCodes.LetterInUse));
    }

    [Fact]
    public void Two_steps_cant_claim_the_same_letter()
    {
        var x = new DriveAssignment(new DriveLetter('X'));
        var plan = Plan(
            new CreatePartition(3, Size: ByteSize.FromGigabytes(10), Format: new FormatSpec(FileSystemKind.Ntfs, "One"), Assign: x),
            new CreatePartition(3, Size: ByteSize.FromGigabytes(10), Format: new FormatSpec(FileSystemKind.Ntfs, "Two"), Assign: x));

        Assert.True(Blocked(plan, IssueCodes.LetterInUse));
    }

    [Fact]
    public void A_letter_freed_by_clean_can_be_reused()
    {
        var options = new WipeAndReuseOptions { Assign = new DriveAssignment(new DriveLetter('E')) };
        var plan = Plan([.. WipeAndReuse.Build(2, options)]);

        Assert.True(plan.CanRun);
    }

    [Fact]
    public void Assigning_when_there_is_already_a_letter_needs_replacing()
    {
        var plan = Plan(new AssignDriveLetter(3, 1, new DriveLetter('Y')));

        Assert.True(Blocked(plan, IssueCodes.AlreadyHasLetter));
    }

    [Fact]
    public void Changing_a_letter_works()
    {
        var plan = Plan(new AssignDriveLetter(3, 1, new DriveLetter('Y'), Replacing: new DriveLetter('F')));

        Assert.True(plan.CanRun);
    }

    [Fact]
    public void Efi_partitions_need_gpt()
    {
        var plan = Plan(new CreatePartition(2, NewPartitionKind.Efi, ByteSize.FromMegabytes(100)));

        Assert.True(Blocked(plan, IssueCodes.GptOnly));
    }

    [Fact]
    public void Creating_more_than_the_free_space_is_blocked()
    {
        var plan = Plan(new CreatePartition(3, Size: ByteSize.FromGigabytes(700)));

        Assert.True(Blocked(plan, IssueCodes.NotEnoughSpace));
    }

    [Fact]
    public void Creating_on_a_raw_disk_says_it_will_be_mbr()
    {
        var plan = Plan(new CleanDisk(2), new CreatePartition(2, Format: new FormatSpec(FileSystemKind.ExFat, "USB")));

        Assert.Contains(plan.Issues, i => i.Code == IssueCodes.WillBecomeMbr);
        Assert.True(plan.CanRun);
    }

    [Fact]
    public void Shrinking_exfat_is_blocked()
    {
        Assert.True(Blocked(Plan(new ShrinkPartition(4, 1, ByteSize.FromGigabytes(10))), IssueCodes.UnsupportedFileSystem));
    }

    [Fact]
    public void Shrinking_past_the_free_space_is_blocked()
    {
        Assert.True(Blocked(Plan(new ShrinkPartition(3, 1, ByteSize.FromGigabytes(500))), IssueCodes.NotEnoughSpace));
    }

    [Fact]
    public void Shrinking_within_the_free_space_is_fine()
    {
        Assert.True(Plan(new ShrinkPartition(3, 1, ByteSize.FromGigabytes(100))).CanRun);
    }

    [Fact]
    public void Extending_needs_free_space_right_after()
    {
        Assert.True(Blocked(Plan(new ExtendPartition(3, 1)), IssueCodes.NoSpaceAfter));
        Assert.True(Plan(new ExtendPartition(3, 2)).CanRun);
        Assert.True(Blocked(Plan(new ExtendPartition(3, 2, ByteSize.FromGigabytes(700))), IssueCodes.NotEnoughSpace));
    }

    [Fact]
    public void Disk_ids_must_match_the_disk_style()
    {
        Assert.True(Blocked(Plan(new SetDiskUniqueId(3, "1A2B3C4D")), IssueCodes.InvalidDiskId));
        Assert.True(Plan(new SetDiskUniqueId(3, "6F1C2D3E-1A2B-4C5D-8E9F-0A1B2C3D4E5F")).CanRun);
        Assert.True(Plan(new SetDiskUniqueId(2, "1A2B3C4D")).CanRun);
        Assert.True(Blocked(Plan(new SetDiskUniqueId(2, "00000000")), IssueCodes.InvalidDiskId));
    }

    [Fact]
    public void Gpt_attributes_need_a_gpt_disk()
    {
        Assert.True(Blocked(Plan(new SetGptAttributes(2, 1, GptAttributeFlags.Hidden)), IssueCodes.GptOnly));
    }

    [Fact]
    public void Partition_type_has_to_fit_the_disk()
    {
        Assert.True(Blocked(Plan(new SetPartitionType(3, 2, PartitionTypes.MbrLinux)), IssueCodes.TypeDoesNotFitDisk));
        Assert.True(Plan(new SetPartitionType(3, 2, PartitionTypes.LinuxFilesystem)).CanRun);
    }

    [Fact]
    public void Offline_disks_have_to_come_online_first()
    {
        var offline = TestDisks.DataDisk with { IsOffline = true };
        var context = new PlanningContext(TestDisks.Snapshot(TestDisks.WindowsDisk, offline));

        var plan = OperationPlanner.Create([new FormatPartition(3, 2, new FormatSpec(FileSystemKind.Ntfs, "Backup"))], context);
        Assert.True(Blocked(plan, IssueCodes.DiskOffline));

        Assert.True(OperationPlanner.Create([new SetDiskOnline(3, true)], context).CanRun);
    }

    [Fact]
    public void Read_only_disks_need_the_flag_cleared_first()
    {
        var locked = TestDisks.UsbStick with { IsReadOnly = true };
        var context = new PlanningContext(TestDisks.Snapshot(TestDisks.WindowsDisk, locked));

        Assert.True(Blocked(OperationPlanner.Create([new CleanDisk(2)], context), IssueCodes.DiskReadOnly));
        Assert.True(OperationPlanner.Create([new SetDiskReadOnly(2, false)], context).CanRun);
    }

    [Fact]
    public void Virtual_disks_wont_overwrite_files()
    {
        var context = TestDisks.Context(fileExists: _ => true);
        var plan = OperationPlanner.Create([new CreateVirtualDisk(@"C:\VMs\test.vhdx", ByteSize.FromGigabytes(20))], context);

        Assert.True(Blocked(plan, IssueCodes.FileExists));
    }

    [Theory]
    [InlineData("test.vhdx")]
    [InlineData(@"C:\VMs\test.iso")]
    [InlineData(@"C:\VMs\te""st.vhdx")]
    public void Virtual_disk_paths_must_be_full_vhd_paths(string path)
    {
        Assert.True(Blocked(Plan(new CreateVirtualDisk(path, ByteSize.FromGigabytes(1))), IssueCodes.InvalidPath));
    }

    [Fact]
    public void Vhd_files_have_a_size_limit_and_vhdx_files_a_bigger_one()
    {
        Assert.True(Blocked(Plan(new CreateVirtualDisk(@"C:\VMs\big.vhd", ByteSize.FromTerabytes(3))), IssueCodes.VirtualDiskTooBig));
        Assert.True(Plan(new CreateVirtualDisk(@"C:\VMs\big.vhdx", ByteSize.FromTerabytes(3))).CanRun);
    }

    [Fact]
    public void Attaching_needs_the_file_to_exist()
    {
        Assert.True(Blocked(Plan(new AttachVirtualDisk(@"C:\VMs\missing.vhdx")), IssueCodes.FileMissing));
    }

    [Fact]
    public void Detaching_needs_it_to_be_attached()
    {
        Assert.True(Blocked(Plan(new DetachVirtualDisk(@"C:\VMs\test.vhdx")), IssueCodes.NotAttached));
    }

    [Fact]
    public void Detaching_an_attached_disk_warns_about_its_letters()
    {
        var vhd = new DiskInfo
        {
            Number = 5,
            FriendlyName = "Msft Virtual Disk",
            Size = ByteSize.FromGigabytes(20),
            Bus = BusType.FileBackedVirtual,
            Location = @"C:\VMs\test.vhdx",
            Style = PartitionStyle.Gpt,
            Partitions =
            [
                new PartitionInfo { DiskNumber = 5, Number = 1, Size = ByteSize.FromGigabytes(20), Type = PartitionTypes.BasicData, Letter = new DriveLetter('V') },
            ],
        };
        var context = new PlanningContext(TestDisks.Snapshot(TestDisks.WindowsDisk, vhd));

        var plan = OperationPlanner.Create([new DetachVirtualDisk(@"c:/vms/TEST.vhdx")], context);

        Assert.True(plan.CanRun);
        Assert.Contains(plan.Issues, i => i.Code == IssueCodes.DetachInUse);
    }
}
