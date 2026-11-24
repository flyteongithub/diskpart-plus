namespace DiskpartPlus.Core.Models;

public enum PartitionStyle
{
    /// <summary>Not initialised yet. A disk is like this straight after "clean".</summary>
    Raw,
    Mbr,
    Gpt,
}

// The numbers match MSFT_Disk.BusType so the Windows layer can cast straight across.
public enum BusType
{
    Unknown = 0,
    Scsi = 1,
    Atapi = 2,
    Ata = 3,
    Ieee1394 = 4,
    Ssa = 5,
    FibreChannel = 6,
    Usb = 7,
    Raid = 8,
    Iscsi = 9,
    Sas = 10,
    Sata = 11,
    SecureDigital = 12,
    Mmc = 13,
    Virtual = 14,
    FileBackedVirtual = 15,
    StorageSpaces = 16,
    Nvme = 17,
    Scm = 18,
    Ufs = 19,
}

// The numbers match MSFT_PhysicalDisk.MediaType.
public enum MediaType
{
    Unknown = 0,
    Hdd = 3,
    Ssd = 4,
    Scm = 5,
}

public enum VirtualDiskAllocation
{
    /// <summary>Starts small and grows as you write to it.</summary>
    Expandable,

    /// <summary>Takes up the full size on disk straight away.</summary>
    Fixed,
}

public enum NewPartitionKind
{
    Primary,
    Efi,
    MicrosoftReserved,
}

/// <summary>The GPT attribute bits Windows cares about.</summary>
[Flags]
public enum GptAttributeFlags : ulong
{
    None = 0,
    RequiredPartition = 0x0000000000000001,
    ReadOnly = 0x1000000000000000,
    ShadowCopy = 0x2000000000000000,
    Hidden = 0x4000000000000000,
    NoDriveLetter = 0x8000000000000000,
}
