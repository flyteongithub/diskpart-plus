using System.Management;
using System.Runtime.Versioning;
using DiskpartPlus.Core.Execution;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Partitions;

namespace DiskpartPlus.Windows.Inventory;

/// <summary>
/// Reads every disk through the Windows storage management API (the same thing Get-Disk uses).
/// We never parse diskpart's own output for this, because it's translated into the Windows
/// display language and it rounds sizes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CimStorageInventory : IStorageInventory
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";
    private const string Cimv2Scope = @"root\cimv2";
    private const string BitLockerScope = @"root\cimv2\Security\MicrosoftVolumeEncryption";

    public Task<StorageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private static StorageSnapshot Read(CancellationToken cancellationToken)
    {
        var mediaByDisk = ReadMediaTypes();
        cancellationToken.ThrowIfCancellationRequested();

        var volumes = Cim.Query(StorageScope, "SELECT * FROM MSFT_Volume").Select(ToVolume).ToList();
        cancellationToken.ThrowIfCancellationRequested();

        var pagefileLetters = ReadPagefileLetters();
        var bitLockerLetters = ReadBitLockerLetters();
        var appLetter = LetterOf(AppContext.BaseDirectory);

        var partitionsByDisk = Cim.Query(StorageScope, "SELECT * FROM MSFT_Partition")
            .Select(p => ToPartition(p, volumes, pagefileLetters, bitLockerLetters, appLetter))
            .GroupBy(p => p.DiskNumber)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PartitionInfo>)g.OrderBy(p => p.Offset).ToList());
        cancellationToken.ThrowIfCancellationRequested();

        var disks = Cim.Query(StorageScope, "SELECT * FROM MSFT_Disk")
            .Select(d => ToDisk(d, mediaByDisk, partitionsByDisk))
            .OrderBy(d => d.Number)
            .ToList();

        var partitionLetters = disks.SelectMany(d => d.Partitions).Select(p => p.Letter).OfType<DriveLetter>().ToHashSet();
        var otherLetters = DriveInfo.GetDrives()
            .Select(d => LetterOf(d.Name))
            .OfType<DriveLetter>()
            .Where(l => !partitionLetters.Contains(l))
            .ToList();

        return new StorageSnapshot { Disks = disks, OtherLettersInUse = otherLetters, TakenAt = DateTimeOffset.Now };
    }

    private static DiskInfo ToDisk(
        ManagementBaseObject d,
        IReadOnlyDictionary<int, MediaType> mediaByDisk,
        IReadOnlyDictionary<int, IReadOnlyList<PartitionInfo>> partitionsByDisk)
    {
        var number = Cim.Int(d, "Number");
        var partitions = partitionsByDisk.GetValueOrDefault(number) ?? [];

        return new DiskInfo
        {
            Number = number,
            FriendlyName = Cim.String(d, "FriendlyName") ?? Cim.String(d, "Model") ?? $"Disk {number}",
            Size = new ByteSize(Cim.Long(d, "Size")),
            UniqueId = Cim.String(d, "UniqueId"),
            SerialNumber = Cim.String(d, "SerialNumber"),
            Style = Cim.Int(d, "PartitionStyle") switch
            {
                1 => PartitionStyle.Mbr,
                2 => PartitionStyle.Gpt,
                _ => PartitionStyle.Raw,
            },
            Bus = Enum.IsDefined((BusType)Cim.Int(d, "BusType")) ? (BusType)Cim.Int(d, "BusType") : BusType.Unknown,
            Media = mediaByDisk.GetValueOrDefault(number, MediaType.Unknown),
            LargestFreeExtent = new ByteSize(Cim.Long(d, "LargestFreeExtent")),
            IsSystem = Cim.Bool(d, "IsSystem"),
            IsBoot = Cim.Bool(d, "IsBoot"),
            IsReadOnly = Cim.Bool(d, "IsReadOnly"),
            IsOffline = Cim.Bool(d, "IsOffline"),
            Location = Cim.String(d, "Location"),
            HoldsPagefile = partitions.Any(p => p.HoldsPagefile),
            HostsApplication = partitions.Any(p => p.HostsApplication),
            Partitions = partitions,
        };
    }

    private static PartitionInfo ToPartition(
        ManagementBaseObject p,
        IReadOnlyList<(string Path, VolumeInfo Volume)> volumes,
        IReadOnlySet<DriveLetter> pagefileLetters,
        IReadOnlySet<DriveLetter> bitLockerLetters,
        DriveLetter? appLetter)
    {
        var letter = Cim.Letter(p, "DriveLetter") is { } c ? new DriveLetter(c) : (DriveLetter?)null;

        // A partition lists its volume's \\?\Volume{...}\ path among its access paths.
        var accessPaths = Cim.Strings(p, "AccessPaths");
        var volume = volumes
            .Where(v => accessPaths.Any(a => string.Equals(a, v.Path, StringComparison.OrdinalIgnoreCase)))
            .Select(v => v.Volume)
            .FirstOrDefault();

        if (volume is not null && letter is { } l && bitLockerLetters.Contains(l))
        {
            volume = volume with { IsBitLockerProtected = true };
        }

        var attributes = GptAttributeFlags.None;
        if (Cim.Bool(p, "IsHidden")) attributes |= GptAttributeFlags.Hidden;
        if (Cim.Bool(p, "IsReadOnly")) attributes |= GptAttributeFlags.ReadOnly;
        if (Cim.Bool(p, "IsShadowCopy")) attributes |= GptAttributeFlags.ShadowCopy;
        if (Cim.Bool(p, "NoDefaultDriveLetter")) attributes |= GptAttributeFlags.NoDriveLetter;

        return new PartitionInfo
        {
            DiskNumber = Cim.Int(p, "DiskNumber"),
            Number = Cim.Int(p, "PartitionNumber"),
            Offset = new ByteSize(Cim.Long(p, "Offset")),
            Size = new ByteSize(Cim.Long(p, "Size")),
            Type = TypeOf(p),
            IsSystem = Cim.Bool(p, "IsSystem"),
            IsBoot = Cim.Bool(p, "IsBoot"),
            IsActive = Cim.Bool(p, "IsActive"),
            IsHidden = Cim.Bool(p, "IsHidden"),
            HoldsPagefile = letter is { } pl && pagefileLetters.Contains(pl),
            HostsApplication = letter is not null && letter == appLetter,
            GptAttributes = attributes,
            Letter = letter,
            Volume = volume,
        };
    }

    private static PartitionType TypeOf(ManagementBaseObject p)
    {
        if (Cim.String(p, "GptType") is { } gpt && Guid.TryParse(gpt, out var id))
        {
            return PartitionTypes.FromGpt(id);
        }

        var mbr = Cim.Long(p, "MbrType");
        return mbr is > 0 and <= 255
            ? PartitionTypes.FromMbr((byte)mbr)
            : new PartitionType { Name = "Unknown", Family = PartitionFamily.Unknown };
    }

    private static (string Path, VolumeInfo Volume) ToVolume(ManagementBaseObject v)
    {
        var fileSystemName = Cim.String(v, "FileSystem");
        return (Cim.String(v, "Path") ?? string.Empty, new VolumeInfo
        {
            Label = Cim.String(v, "FileSystemLabel"),
            FileSystem = FileSystems.Parse(fileSystemName),
            FileSystemName = fileSystemName,
            Size = new ByteSize(Cim.Long(v, "Size")),
            FreeSpace = new ByteSize(Cim.Long(v, "SizeRemaining")),
        });
    }

    private static Dictionary<int, MediaType> ReadMediaTypes()
    {
        var result = new Dictionary<int, MediaType>();
        foreach (var disk in Cim.Query(StorageScope, "SELECT * FROM MSFT_PhysicalDisk"))
        {
            // For physical disks the device ID is the same number diskpart uses.
            if (int.TryParse(Cim.String(disk, "DeviceId"), out var number))
            {
                var media = (MediaType)Cim.Int(disk, "MediaType");
                result[number] = Enum.IsDefined(media) ? media : MediaType.Unknown;
            }
        }

        return result;
    }

    private static HashSet<DriveLetter> ReadPagefileLetters()
    {
        try
        {
            return Cim.Query(Cimv2Scope, "SELECT Name FROM Win32_PageFileUsage")
                .Select(p => LetterOf(Cim.String(p, "Name")))
                .OfType<DriveLetter>()
                .ToHashSet();
        }
        catch (ManagementException)
        {
            return [];
        }
    }

    private static HashSet<DriveLetter> ReadBitLockerLetters()
    {
        // Not every edition of Windows has BitLocker, so this namespace might not exist.
        try
        {
            return Cim.Query(BitLockerScope, "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume")
                .Where(v => Cim.Long(v, "ProtectionStatus") == 1)
                .Select(v => LetterOf(Cim.String(v, "DriveLetter")))
                .OfType<DriveLetter>()
                .ToHashSet();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }

    private static DriveLetter? LetterOf(string? path) =>
        path is { Length: >= 2 } && path[1] == ':' && DriveLetter.TryParse(path[..1], out var letter) ? letter : null;
}
