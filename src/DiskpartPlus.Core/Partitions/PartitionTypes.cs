namespace DiskpartPlus.Core.Partitions;

/// <summary>
/// The partition types we know by name. Anything else still works, it just shows up as
/// "Unknown" with its raw ID.
/// </summary>
public static class PartitionTypes
{
    // Windows (GPT)
    public static readonly PartitionType EfiSystem = Gpt("EFI system", PartitionFamily.Windows, "C12A7328-F81F-11D2-BA4B-00A0C93EC93B", notes: "Holds the boot loader");
    public static readonly PartitionType MicrosoftReserved = Gpt("Microsoft reserved", PartitionFamily.Windows, "E3C9E316-0B5C-4DB8-817D-F92DF00215AE", notes: "Small hidden partition Windows keeps for itself");
    public static readonly PartitionType BasicData = Gpt("Basic data", PartitionFamily.Windows, "EBD0A0A2-B9E5-4433-87C0-68B6B72699C7", data: true);
    public static readonly PartitionType WindowsRecovery = Gpt("Windows recovery", PartitionFamily.Windows, "DE94BBA4-06D1-4D40-A16A-BFD50179D6AC", notes: "Holds the Windows recovery tools");
    public static readonly PartitionType LdmMetadata = Gpt("Dynamic disk metadata", PartitionFamily.Windows, "5808C8AA-7E8F-42E0-85D2-E1E90434CFB3");
    public static readonly PartitionType LdmData = Gpt("Dynamic disk data", PartitionFamily.Windows, "AF9B60A0-1431-4F62-BC68-3311714A69AD");
    public static readonly PartitionType StorageSpaces = Gpt("Storage Spaces", PartitionFamily.Windows, "E75CAF8F-F680-4CEE-AFA3-B001E56EFC2D");

    // Apple (GPT). Windows can't read any of these.
    public static readonly PartitionType Apfs = Gpt("APFS container", PartitionFamily.Apple, "7C3457EF-0000-11AA-AA11-00306543ECAC", notes: "Modern Mac file system");
    public static readonly PartitionType HfsPlus = Gpt("Mac OS Extended (HFS+)", PartitionFamily.Apple, "48465300-0000-11AA-AA11-00306543ECAC", notes: "Older Mac file system");
    public static readonly PartitionType AppleBoot = Gpt("Apple boot (Recovery HD)", PartitionFamily.Apple, "426F6F74-0000-11AA-AA11-00306543ECAC");
    public static readonly PartitionType AppleCoreStorage = Gpt("Apple Core Storage", PartitionFamily.Apple, "53746F72-6167-11AA-AA11-00306543ECAC", notes: "Used by Fusion Drives and older FileVault");
    public static readonly PartitionType AppleRaid = Gpt("Apple RAID", PartitionFamily.Apple, "52414944-0000-11AA-AA11-00306543ECAC");
    public static readonly PartitionType AppleRaidOffline = Gpt("Apple RAID (offline)", PartitionFamily.Apple, "52414944-5F4F-11AA-AA11-00306543ECAC");
    public static readonly PartitionType AppleLabel = Gpt("Apple label", PartitionFamily.Apple, "4C616265-6C00-11AA-AA11-00306543ECAC");

    // Linux (GPT)
    public static readonly PartitionType LinuxFilesystem = Gpt("Linux file system", PartitionFamily.Linux, "0FC63DAF-8483-4772-8E79-3D69D8477DE4");
    public static readonly PartitionType LinuxRootX64 = Gpt("Linux root (x86-64)", PartitionFamily.Linux, "4F68BCE3-E8CD-4DB1-96E7-FBCAF984B709");
    public static readonly PartitionType LinuxHome = Gpt("Linux home", PartitionFamily.Linux, "933AC7E1-2EB4-4F13-B844-0E14E2AEF915");
    public static readonly PartitionType LinuxSwap = Gpt("Linux swap", PartitionFamily.Linux, "0657FD6D-A4AB-43C4-84E5-0933C84B4F4F");
    public static readonly PartitionType LinuxLvm = Gpt("Linux LVM", PartitionFamily.Linux, "E6D6D379-F507-44C2-A23C-238F2A3DF928");

    // Other (GPT)
    public static readonly PartitionType BiosBoot = Gpt("BIOS boot", PartitionFamily.Other, "21686148-6449-6E6F-744E-656564454649", notes: "Used by GRUB on BIOS machines");

    // MBR
    public static readonly PartitionType MbrNtfsOrExFat = Mbr("NTFS / exFAT", PartitionFamily.Windows, 0x07, data: true);
    public static readonly PartitionType MbrFat32 = Mbr("FAT32", PartitionFamily.Windows, 0x0B, data: true);
    public static readonly PartitionType MbrFat32Lba = Mbr("FAT32 (LBA)", PartitionFamily.Windows, 0x0C, data: true);
    public static readonly PartitionType MbrFat16 = Mbr("FAT16", PartitionFamily.Windows, 0x06, data: true);
    public static readonly PartitionType MbrFat16Lba = Mbr("FAT16 (LBA)", PartitionFamily.Windows, 0x0E, data: true);
    public static readonly PartitionType MbrFat12 = Mbr("FAT12", PartitionFamily.Windows, 0x01, data: true);
    public static readonly PartitionType MbrExtended = Mbr("Extended", PartitionFamily.Windows, 0x05);
    public static readonly PartitionType MbrExtendedLba = Mbr("Extended (LBA)", PartitionFamily.Windows, 0x0F);
    public static readonly PartitionType MbrWindowsRecovery = Mbr("Windows recovery", PartitionFamily.Windows, 0x27, notes: "Holds the Windows recovery tools");
    public static readonly PartitionType MbrDynamic = Mbr("Dynamic disk", PartitionFamily.Windows, 0x42);
    public static readonly PartitionType MbrEfiSystem = Mbr("EFI system", PartitionFamily.Windows, 0xEF);
    public static readonly PartitionType MbrGptProtective = Mbr("GPT protective", PartitionFamily.Other, 0xEE);
    public static readonly PartitionType MbrHfsPlus = Mbr("Mac OS Extended (HFS+)", PartitionFamily.Apple, 0xAF);
    public static readonly PartitionType MbrAppleBoot = Mbr("Apple boot", PartitionFamily.Apple, 0xAB);
    public static readonly PartitionType MbrLinux = Mbr("Linux", PartitionFamily.Linux, 0x83);
    public static readonly PartitionType MbrLinuxSwap = Mbr("Linux swap", PartitionFamily.Linux, 0x82);
    public static readonly PartitionType MbrLinuxLvm = Mbr("Linux LVM", PartitionFamily.Linux, 0x8E);

    private static readonly Dictionary<Guid, PartitionType> ByGptId;
    private static readonly Dictionary<byte, PartitionType> ByMbrId;

    static PartitionTypes()
    {
        All =
        [
            EfiSystem, MicrosoftReserved, BasicData, WindowsRecovery, LdmMetadata, LdmData, StorageSpaces,
            Apfs, HfsPlus, AppleBoot, AppleCoreStorage, AppleRaid, AppleRaidOffline, AppleLabel,
            LinuxFilesystem, LinuxRootX64, LinuxHome, LinuxSwap, LinuxLvm,
            BiosBoot,
            MbrNtfsOrExFat, MbrFat32, MbrFat32Lba, MbrFat16, MbrFat16Lba, MbrFat12, MbrExtended, MbrExtendedLba,
            MbrWindowsRecovery, MbrDynamic, MbrEfiSystem, MbrGptProtective, MbrHfsPlus, MbrAppleBoot,
            MbrLinux, MbrLinuxSwap, MbrLinuxLvm,
        ];

        ByGptId = All.Where(t => t.GptId is not null).ToDictionary(t => t.GptId!.Value);
        ByMbrId = All.Where(t => t.MbrId is not null).ToDictionary(t => t.MbrId!.Value);
    }

    public static IReadOnlyList<PartitionType> All { get; }

    public static PartitionType FromGpt(Guid id) =>
        ByGptId.TryGetValue(id, out var known)
            ? known
            : new PartitionType { Name = "Unknown", Family = PartitionFamily.Unknown, GptId = id };

    public static PartitionType FromMbr(byte id) =>
        ByMbrId.TryGetValue(id, out var known)
            ? known
            : new PartitionType { Name = "Unknown", Family = PartitionFamily.Unknown, MbrId = id };

    private static PartitionType Gpt(string name, PartitionFamily family, string id, bool data = false, string? notes = null) =>
        new() { Name = name, Family = family, GptId = Guid.Parse(id), IsDataPartition = data, Notes = notes };

    private static PartitionType Mbr(string name, PartitionFamily family, byte id, bool data = false, string? notes = null) =>
        new() { Name = name, Family = family, MbrId = id, IsDataPartition = data, Notes = notes };
}
