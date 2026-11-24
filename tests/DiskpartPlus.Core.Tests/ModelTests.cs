using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Partitions;

namespace DiskpartPlus.Core.Tests;

public class ByteSizeTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(536870912L, "512 MB")]
    [InlineData(500107862016L, "465.8 GB")]
    [InlineData(2199023255552L, "2 TB")]
    public void Shows_sizes_the_way_Windows_does(long bytes, string expected)
    {
        Assert.Equal(expected, new ByteSize(bytes).ToString());
    }

    [Fact]
    public void Whole_megabytes_rounds_down()
    {
        Assert.Equal(2, new ByteSize(3 * ByteSize.Megabyte - 1).WholeMegabytes);
        Assert.Equal(10240, ByteSize.FromGigabytes(10).WholeMegabytes);
    }

    [Fact]
    public void Compares_and_adds()
    {
        var small = ByteSize.FromMegabytes(100);
        var big = ByteSize.FromGigabytes(1);

        Assert.True(small < big);
        Assert.True(big >= small);
        Assert.Equal(ByteSize.FromMegabytes(1124), small + big);
        Assert.Equal(big, ByteSize.Max(small, big));
    }
}

public class DriveLetterTests
{
    [Theory]
    [InlineData("e", 'E')]
    [InlineData("E:", 'E')]
    [InlineData("E:\\", 'E')]
    [InlineData(" z ", 'Z')]
    public void Parses_the_usual_ways_of_writing_a_letter(string text, char expected)
    {
        Assert.True(DriveLetter.TryParse(text, out var letter));
        Assert.Equal(expected, letter.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("EF")]
    [InlineData("1")]
    [InlineData("é")]
    public void Rejects_things_that_arent_letters(string text)
    {
        Assert.False(DriveLetter.TryParse(text, out _));
    }

    [Fact]
    public void Constructor_rejects_non_letters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DriveLetter('1'));
    }
}

public class FileSystemTests
{
    [Theory]
    [InlineData("NTFS", FileSystemKind.Ntfs)]
    [InlineData("exFAT", FileSystemKind.ExFat)]
    [InlineData("FAT32", FileSystemKind.Fat32)]
    [InlineData("RAW", FileSystemKind.None)]
    [InlineData("", FileSystemKind.None)]
    [InlineData("APFS", FileSystemKind.Unknown)]
    public void Parses_names_windows_reports(string name, FileSystemKind expected)
    {
        Assert.Equal(expected, FileSystems.Parse(name));
    }

    [Fact]
    public void Knows_which_label_characters_each_file_system_accepts()
    {
        Assert.Equal('.', FileSystemKind.Fat32.FindBadLabelChar("MY.DRIVE"));
        Assert.Null(FileSystemKind.ExFat.FindBadLabelChar("MY.DRIVE"));
        Assert.Equal('"', FileSystemKind.Ntfs.FindBadLabelChar("a\"b"));
        Assert.Null(FileSystemKind.Ntfs.FindBadLabelChar("Games: 2024"));
    }
}

public class PartitionTypeTests
{
    [Fact]
    public void Recognises_apfs()
    {
        var type = PartitionTypes.FromGpt(Guid.Parse("7C3457EF-0000-11AA-AA11-00306543ECAC"));

        Assert.Same(PartitionTypes.Apfs, type);
        Assert.Equal(PartitionFamily.Apple, type.Family);
        Assert.True(type.NeedsOverrideToDelete);
    }

    [Fact]
    public void Basic_data_can_be_deleted_without_override()
    {
        var type = PartitionTypes.FromGpt(Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"));

        Assert.True(type.IsDataPartition);
        Assert.False(type.NeedsOverrideToDelete);
    }

    [Fact]
    public void Unknown_types_keep_their_id()
    {
        var id = Guid.NewGuid();
        var type = PartitionTypes.FromGpt(id);

        Assert.Equal("Unknown", type.Name);
        Assert.Equal(id, type.GptId);
        Assert.True(type.NeedsOverrideToDelete);
    }

    [Fact]
    public void Recognises_mbr_types()
    {
        Assert.Same(PartitionTypes.MbrNtfsOrExFat, PartitionTypes.FromMbr(0x07));
        Assert.Equal(PartitionFamily.Apple, PartitionTypes.FromMbr(0xAF).Family);
    }

    [Fact]
    public void Gives_diskpart_the_right_id_for_the_disk_style()
    {
        Assert.Equal("c12a7328-f81f-11d2-ba4b-00a0c93ec93b", PartitionTypes.EfiSystem.DiskpartId(PartitionStyle.Gpt));
        Assert.Null(PartitionTypes.EfiSystem.DiskpartId(PartitionStyle.Mbr));
        Assert.Equal("27", PartitionTypes.MbrWindowsRecovery.DiskpartId(PartitionStyle.Mbr));
    }

    [Fact]
    public void Every_known_type_has_a_unique_id()
    {
        var gpt = PartitionTypes.All.Where(t => t.GptId is not null).Select(t => t.GptId).ToList();
        var mbr = PartitionTypes.All.Where(t => t.MbrId is not null).Select(t => t.MbrId).ToList();

        Assert.Equal(gpt.Count, gpt.Distinct().Count());
        Assert.Equal(mbr.Count, mbr.Distinct().Count());
    }
}
