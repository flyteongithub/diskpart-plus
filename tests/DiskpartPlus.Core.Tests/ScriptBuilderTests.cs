using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Scripting;

namespace DiskpartPlus.Core.Tests;

public class ScriptBuilderTests
{
    private static readonly DriveLetter E = new('E');
    private static readonly DriveLetter F = new('F');
    private static readonly DriveLetter X = new('X');

    [Fact]
    public void Clean()
    {
        Assert.Equal(new[] { "select disk 2", "clean" }, DiskpartScriptBuilder.CommandsFor(new CleanDisk(2)));
        Assert.Equal(new[] { "select disk 2", "clean all" }, DiskpartScriptBuilder.CommandsFor(new CleanDisk(2, ZeroEverySector: true)));
    }

    [Fact]
    public void Convert()
    {
        Assert.Equal(new[] { "select disk 1", "convert gpt" }, DiskpartScriptBuilder.CommandsFor(new ConvertPartitionStyle(1, PartitionStyle.Gpt)));
        Assert.Equal(new[] { "select disk 1", "convert mbr" }, DiskpartScriptBuilder.CommandsFor(new ConvertPartitionStyle(1, PartitionStyle.Mbr)));
    }

    [Fact]
    public void Create_partition_formats_and_assigns_in_one_go()
    {
        var operation = new CreatePartition(1, Format: new FormatSpec(FileSystemKind.ExFat, "DATA"), Assign: new DriveAssignment(X));

        Assert.Equal(
            new[] { "select disk 1", "create partition primary", "format fs=exfat label=\"DATA\" quick", "assign letter=X" },
            DiskpartScriptBuilder.CommandsFor(operation));
    }

    [Fact]
    public void Create_efi_partition_with_size()
    {
        var operation = new CreatePartition(0, NewPartitionKind.Efi, ByteSize.FromMegabytes(100));

        Assert.Equal(new[] { "select disk 0", "create partition efi size=100" }, DiskpartScriptBuilder.CommandsFor(operation));
    }

    [Fact]
    public void Format_existing_partition_forces_dismount()
    {
        var operation = new FormatPartition(3, 1, new FormatSpec(FileSystemKind.Ntfs, "Games"));

        Assert.Equal(
            new[] { "select disk 3", "select partition 1", "format fs=ntfs label=\"Games\" quick override" },
            DiskpartScriptBuilder.CommandsFor(operation));
    }

    [Fact]
    public void Format_with_no_label_and_full_format()
    {
        var operation = new FormatPartition(3, 1, new FormatSpec(FileSystemKind.Ntfs, "", Quick: false, AllocationUnitBytes: 4096));

        Assert.Equal(
            new[] { "select disk 3", "select partition 1", "format fs=ntfs unit=4096 override" },
            DiskpartScriptBuilder.CommandsFor(operation));
    }

    [Fact]
    public void Delete_with_and_without_override()
    {
        Assert.Equal("delete partition", DiskpartScriptBuilder.CommandsFor(new DeletePartition(3, 2))[^1]);
        Assert.Equal("delete partition override", DiskpartScriptBuilder.CommandsFor(new DeletePartition(1, 1, Override: true))[^1]);
    }

    [Fact]
    public void Shrink_asks_for_an_exact_amount()
    {
        var lines = DiskpartScriptBuilder.CommandsFor(new ShrinkPartition(3, 1, ByteSize.FromGigabytes(10)));

        Assert.Equal("shrink desired=10240 minimum=10240", lines[^1]);
    }

    [Fact]
    public void Extend()
    {
        Assert.Equal("extend", DiskpartScriptBuilder.CommandsFor(new ExtendPartition(3, 2))[^1]);
        Assert.Equal("extend size=1024", DiskpartScriptBuilder.CommandsFor(new ExtendPartition(3, 2, ByteSize.FromGigabytes(1)))[^1]);
    }

    [Fact]
    public void Changing_a_letter_removes_the_old_one_first()
    {
        var operation = new AssignDriveLetter(2, 1, F, Replacing: E);

        Assert.Equal(
            new[] { "select disk 2", "select partition 1", "remove letter=E", "assign letter=F" },
            DiskpartScriptBuilder.CommandsFor(operation));
    }

    [Fact]
    public void Assign_next_free_letter()
    {
        Assert.Equal("assign", DiskpartScriptBuilder.CommandsFor(new AssignDriveLetter(3, 2))[^1]);
    }

    [Fact]
    public void Remove_letter()
    {
        Assert.Equal(
            new[] { "select disk 2", "select partition 1", "remove letter=E" },
            DiskpartScriptBuilder.CommandsFor(new RemoveDriveLetter(2, 1, E)));
    }

    [Fact]
    public void Set_partition_type_uses_guid_for_gpt_and_hex_for_mbr()
    {
        Assert.Equal(
            "set id=de94bba4-06d1-4d40-a16a-bfd50179d6ac override",
            DiskpartScriptBuilder.CommandsFor(new SetPartitionType(3, 2, PartitionTypes.WindowsRecovery))[^1]);

        Assert.Equal(
            "set id=27 override",
            DiskpartScriptBuilder.CommandsFor(new SetPartitionType(2, 1, PartitionTypes.MbrWindowsRecovery))[^1]);
    }

    [Fact]
    public void Gpt_attributes_are_written_as_16_hex_digits()
    {
        var flags = GptAttributeFlags.RequiredPartition | GptAttributeFlags.NoDriveLetter;

        Assert.Equal("gpt attributes=0x8000000000000001", DiskpartScriptBuilder.CommandsFor(new SetGptAttributes(3, 2, flags))[^1]);
    }

    [Fact]
    public void Disk_attributes_and_ids()
    {
        Assert.Equal(new[] { "select disk 2", "attributes disk clear readonly" }, DiskpartScriptBuilder.CommandsFor(new SetDiskReadOnly(2, false)));
        Assert.Equal(new[] { "select disk 3", "offline disk" }, DiskpartScriptBuilder.CommandsFor(new SetDiskOnline(3, false)));
        Assert.Equal(new[] { "select disk 2", "uniqueid disk id=1A2B3C4D" }, DiskpartScriptBuilder.CommandsFor(new SetDiskUniqueId(2, "1A2B3C4D")));
    }

    [Fact]
    public void Create_virtual_disk_and_set_it_up()
    {
        var operation = new CreateVirtualDisk(
            @"C:\VMs\test.vhdx",
            ByteSize.FromGigabytes(20),
            Setup: new VirtualDiskSetup(PartitionStyle.Gpt, new FormatSpec(FileSystemKind.Ntfs, "Test"), DriveAssignment.NextFree));

        Assert.Equal(
            new[]
            {
                "create vdisk file=\"C:\\VMs\\test.vhdx\" maximum=20480 type=expandable",
                "select vdisk file=\"C:\\VMs\\test.vhdx\"",
                "attach vdisk",
                "convert gpt",
                "create partition primary",
                "format fs=ntfs label=\"Test\" quick",
                "assign",
            },
            DiskpartScriptBuilder.CommandsFor(operation));
    }

    [Fact]
    public void Attach_and_detach_virtual_disks()
    {
        Assert.Equal(
            new[] { "select vdisk file=\"D:\\old.vhd\"", "attach vdisk readonly" },
            DiskpartScriptBuilder.CommandsFor(new AttachVirtualDisk(@"D:\old.vhd", ReadOnly: true)));

        Assert.Equal(
            new[] { "select vdisk file=\"D:\\old.vhd\"", "detach vdisk" },
            DiskpartScriptBuilder.CommandsFor(new DetachVirtualDisk(@"D:\old.vhd")));
    }

    [Fact]
    public void Full_script_has_a_header_and_a_comment_per_step()
    {
        var script = DiskpartScriptBuilder.Build([new CleanDisk(1), new ConvertPartitionStyle(1, PartitionStyle.Gpt)]);

        Assert.Equal(DiskpartScriptBuilder.Header, script.Lines[0]);
        Assert.Equal("rem Remove all partitions from disk 1 (clean)", script.Lines[1]);
        Assert.Equal(new[] { "select disk 1", "clean", "select disk 1", "convert gpt" }, script.Commands.ToList());
        Assert.EndsWith("\r\n", script.ToText());
        Assert.DoesNotContain("\r\n\r\n", script.ToText());
    }

    [Theory]
    [InlineData("bad\"label")]
    [InlineData("x\nclean")]
    [InlineData("tab\there")]
    public void Labels_cant_break_out_of_the_script(string label)
    {
        var operation = new FormatPartition(3, 1, new FormatSpec(FileSystemKind.Ntfs, label));

        Assert.Throws<ArgumentException>(() => DiskpartScriptBuilder.CommandsFor(operation));
    }

    [Fact]
    public void Disk_ids_with_odd_characters_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => DiskpartScriptBuilder.CommandsFor(new SetDiskUniqueId(2, "1A2B 3C4D")));
    }

    [Fact]
    public void Sizes_under_a_megabyte_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiskpartScriptBuilder.CommandsFor(new CreatePartition(1, Size: new ByteSize(1000))));
    }
}
