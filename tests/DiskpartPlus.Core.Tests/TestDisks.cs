using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Planning;

namespace DiskpartPlus.Core.Tests;

/// <summary>A pretend PC with a few typical disks, so tests read like real situations.</summary>
internal static class TestDisks
{
    private const long Mb = ByteSize.Megabyte;
    private const long Gb = ByteSize.Gigabyte;

    /// <summary>Disk 0: the NVMe drive Windows runs from. Partition 5 is a D: data partition on the same disk.</summary>
    public static DiskInfo WindowsDisk { get; } = new()
    {
        Number = 0,
        FriendlyName = "Samsung SSD 990 PRO 1TB",
        UniqueId = "eui.0025385A11B2C3D4",
        SerialNumber = "S6Z1NJ0W123456",
        Size = new ByteSize(931 * Gb),
        Style = PartitionStyle.Gpt,
        Bus = BusType.Nvme,
        Media = MediaType.Ssd,
        IsSystem = true,
        IsBoot = true,
        HoldsPagefile = true,
        Partitions =
        [
            Part(0, 1, PartitionTypes.EfiSystem, 1 * Mb, 100 * Mb, isSystem: true, volume: Vol(FileSystemKind.Fat32, "")),
            Part(0, 2, PartitionTypes.MicrosoftReserved, 101 * Mb, 16 * Mb),
            Part(0, 3, PartitionTypes.BasicData, 117 * Mb, 700 * Gb, letter: 'C', isBoot: true, pagefile: true, volume: Vol(FileSystemKind.Ntfs, "Windows", free: 300 * Gb)),
            Part(0, 4, PartitionTypes.WindowsRecovery, 700 * Gb + 117 * Mb, 750 * Mb, volume: Vol(FileSystemKind.Ntfs, "")),
            Part(0, 5, PartitionTypes.BasicData, 701 * Gb, 230 * Gb, letter: 'D', volume: Vol(FileSystemKind.Ntfs, "Data", free: 100 * Gb)),
        ],
    };

    /// <summary>Disk 1: an external drive that came from a Mac. EFI plus an APFS container.</summary>
    public static DiskInfo MacDrive { get; } = new()
    {
        Number = 1,
        FriendlyName = "WD My Passport 25E2",
        UniqueId = "USBSTOR\\DISK&VEN_WD&PROD_MY_PASSPORT_25E2\\575837314139",
        SerialNumber = "575837314139",
        Size = new ByteSize(466 * Gb),
        Style = PartitionStyle.Gpt,
        Bus = BusType.Usb,
        Media = MediaType.Hdd,
        Partitions =
        [
            Part(1, 1, PartitionTypes.EfiSystem, 20 * 1024, 200 * Mb, volume: Vol(FileSystemKind.Fat32, "EFI")),
            Part(1, 2, PartitionTypes.Apfs, 200 * Mb + 20 * 1024, 465 * Gb),
        ],
    };

    /// <summary>Disk 2: a USB stick, MBR, one FAT32 partition on E:.</summary>
    public static DiskInfo UsbStick { get; } = new()
    {
        Number = 2,
        FriendlyName = "SanDisk Ultra",
        UniqueId = "USBSTOR\\DISK&VEN_SANDISK&PROD_ULTRA\\4C530001",
        SerialNumber = "4C530001",
        Size = new ByteSize(58 * Gb),
        Style = PartitionStyle.Mbr,
        Bus = BusType.Usb,
        Media = MediaType.Unknown,
        Partitions =
        [
            Part(2, 1, PartitionTypes.MbrFat32Lba, 1 * Mb, 58 * Gb - 1 * Mb, letter: 'E', volume: Vol(FileSystemKind.Fat32, "SANDISK", free: 20 * Gb)),
        ],
    };

    /// <summary>
    /// Disk 3: a big SATA hard drive. F: "Games", then a "Backup" partition with no letter,
    /// then about 663 GB of free space at the end.
    /// </summary>
    public static DiskInfo DataDisk { get; } = new()
    {
        Number = 3,
        FriendlyName = "ST2000DM008-2FR102",
        UniqueId = "SCSI\\DISK&VEN_&PROD_ST2000DM008\\ZFL1ABCD",
        SerialNumber = "ZFL1ABCD",
        Size = new ByteSize(1863 * Gb),
        LargestFreeExtent = new ByteSize(663 * Gb - 1 * Mb),
        Style = PartitionStyle.Gpt,
        Bus = BusType.Sata,
        Media = MediaType.Hdd,
        Partitions =
        [
            Part(3, 1, PartitionTypes.BasicData, 1 * Mb, 1000 * Gb, letter: 'F', volume: Vol(FileSystemKind.Ntfs, "Games", free: 400 * Gb)),
            Part(3, 2, PartitionTypes.BasicData, 1000 * Gb + 1 * Mb, 200 * Gb, volume: Vol(FileSystemKind.Ntfs, "Backup", free: 150 * Gb)),
        ],
    };

    /// <summary>Disk 4: an external SSD with one exFAT partition on G:.</summary>
    public static DiskInfo ExternalSsd { get; } = new()
    {
        Number = 4,
        FriendlyName = "Samsung PSSD T7",
        UniqueId = "USBSTOR\\DISK&VEN_SAMSUNG&PROD_PSSD_T7\\S5TANS0R",
        SerialNumber = "S5TANS0R",
        Size = new ByteSize(931 * Gb),
        Style = PartitionStyle.Gpt,
        Bus = BusType.Usb,
        Media = MediaType.Ssd,
        Partitions =
        [
            Part(4, 1, PartitionTypes.BasicData, 1 * Mb, 931 * Gb - 2 * Mb, letter: 'G', volume: Vol(FileSystemKind.ExFat, "T7", free: 800 * Gb)),
        ],
    };

    public static StorageSnapshot Snapshot(params DiskInfo[] disks) => new()
    {
        Disks = disks.Length > 0 ? disks : [WindowsDisk, MacDrive, UsbStick, DataDisk, ExternalSsd],
    };

    public static PlanningContext Context(Func<string, bool>? fileExists = null) =>
        new(Snapshot()) { FileExists = fileExists ?? (_ => false) };

    private static PartitionInfo Part(
        int disk,
        int number,
        PartitionType type,
        long offset,
        long size,
        char? letter = null,
        bool isSystem = false,
        bool isBoot = false,
        bool pagefile = false,
        VolumeInfo? volume = null) => new()
    {
        DiskNumber = disk,
        Number = number,
        Type = type,
        Offset = new ByteSize(offset),
        Size = new ByteSize(size),
        Letter = letter is { } l ? new DriveLetter(l) : null,
        IsSystem = isSystem,
        IsBoot = isBoot,
        HoldsPagefile = pagefile,
        Volume = volume is null ? null : volume with { Size = new ByteSize(size) },
    };

    private static VolumeInfo Vol(FileSystemKind fileSystem, string label, long free = 0) => new()
    {
        FileSystem = fileSystem,
        Label = label,
        FreeSpace = new ByteSize(free),
    };
}
