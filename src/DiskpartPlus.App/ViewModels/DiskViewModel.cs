using System.Windows;
using System.Windows.Media;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Partitions;

namespace DiskpartPlus.App.ViewModels;

public sealed record Badge(string Text, Brush Brush);

public sealed class DiskViewModel
{
    // Gaps smaller than this are just alignment padding, not worth drawing.
    private static readonly ByteSize SmallestGapShown = ByteSize.FromMegabytes(16);

    public DiskViewModel(DiskInfo disk)
    {
        Disk = disk;
        Partitions = disk.Partitions.Select(p => new PartitionViewModel(p)).ToList();
        Segments = BuildSegments(disk, Partitions);
        Badges = BuildBadges(disk);
    }

    public DiskInfo Disk { get; }

    public int Number => Disk.Number;

    public string Title => Disk.FriendlyName;

    public string Subtitle => $"Disk {Disk.Number} · {Disk.Size} · {StyleText}";

    public string Details => string.Join(" · ", new[] { BusText, MediaText, Disk.IsVirtual ? Disk.Location : null }.Where(s => !string.IsNullOrEmpty(s)));

    public string StyleText => Disk.Style switch
    {
        PartitionStyle.Gpt => "GPT",
        PartitionStyle.Mbr => "MBR",
        _ => "Not set up",
    };

    public string IconText => Disk switch
    {
        { IsVirtual: true } => "VHD",
        { Bus: BusType.Nvme } => "NVMe",
        { Bus: BusType.Usb } => "USB",
        { Media: MediaType.Ssd } => "SSD",
        { Media: MediaType.Hdd } => "HDD",
        _ => "DISK",
    };

    public Brush IconBrush => Palette.ForDisk(Disk);

    public IReadOnlyList<Badge> Badges { get; }

    public IReadOnlyList<PartitionViewModel> Partitions { get; }

    public IReadOnlyList<SegmentViewModel> Segments { get; }

    public bool HasNoPartitions => Partitions.Count == 0;

    public string OnlineActionText => Disk.IsOffline ? "Bring online" : "Take offline";

    public string ReadOnlyActionText => Disk.IsReadOnly ? "Clear read-only" : "Make read-only";

    public string OnlineActionTip => Disk.IsOffline
        ? "Make the disk available to Windows again."
        : "Hide the disk from Windows. The data isn't touched.";

    public string ReadOnlyActionTip => Disk.IsReadOnly
        ? "Clear the read-only flag. Fixes most stuck \"write protected\" drives."
        : "Block all writes to the disk.";

    public Visibility DetachVisibility => Disk.IsVirtual ? Visibility.Visible : Visibility.Collapsed;

    private string? BusText => Disk.Bus switch
    {
        BusType.Nvme => "NVMe",
        BusType.Sata => "SATA",
        BusType.Usb => "USB",
        BusType.Sas => "SAS",
        BusType.Scsi => "SCSI",
        BusType.Raid => "RAID",
        BusType.SecureDigital => "SD card",
        BusType.FileBackedVirtual or BusType.Virtual => "Virtual disk",
        BusType.StorageSpaces => "Storage Spaces",
        _ => null,
    };

    private string? MediaText => Disk.Media switch
    {
        MediaType.Ssd => "SSD",
        MediaType.Hdd => "Hard drive",
        MediaType.Scm => "Storage class memory",
        _ => null,
    };

    private static List<Badge> BuildBadges(DiskInfo disk)
    {
        var badges = new List<Badge>();
        if (disk.IsBoot)
        {
            badges.Add(new Badge("Windows", Palette.Danger));
        }
        else if (disk.IsSystem)
        {
            badges.Add(new Badge("Boot files", Palette.Danger));
        }

        if (disk.HoldsPagefile && !disk.IsBoot)
        {
            badges.Add(new Badge("Page file", Palette.Danger));
        }

        if (disk.HostsApplication)
        {
            badges.Add(new Badge("Diskpart+ runs here", Palette.Danger));
        }

        if (disk.HasApplePartitions)
        {
            badges.Add(new Badge("Mac", Palette.Apple));
        }

        if (disk.Partitions.Any(p => p.Type.Family == PartitionFamily.Linux))
        {
            badges.Add(new Badge("Linux", Palette.Linux));
        }

        if (disk.IsOffline)
        {
            badges.Add(new Badge("Offline", Palette.Muted));
        }

        if (disk.IsReadOnly)
        {
            badges.Add(new Badge("Read-only", Palette.Muted));
        }

        return badges;
    }

    private static List<SegmentViewModel> BuildSegments(DiskInfo disk, IReadOnlyList<PartitionViewModel> partitions)
    {
        var segments = new List<SegmentViewModel>();

        if (disk.Style == PartitionStyle.Raw && partitions.Count == 0)
        {
            segments.Add(SegmentViewModel.Free(disk.Size, "Not set up yet"));
            return segments;
        }

        var cursor = ByteSize.Zero;
        foreach (var partition in partitions.OrderBy(p => p.Partition.Offset))
        {
            var gap = partition.Partition.Offset - cursor;
            if (gap >= SmallestGapShown)
            {
                segments.Add(SegmentViewModel.Free(gap));
            }

            segments.Add(SegmentViewModel.For(partition));
            cursor = ByteSize.Max(cursor, partition.Partition.End);
        }

        var tail = disk.Size - cursor;
        if (tail >= SmallestGapShown)
        {
            segments.Add(SegmentViewModel.Free(tail));
        }

        return segments;
    }
}
