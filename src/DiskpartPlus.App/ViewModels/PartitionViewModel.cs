using System.Windows;
using System.Windows.Media;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Partitions;

namespace DiskpartPlus.App.ViewModels;

public sealed class PartitionViewModel(PartitionInfo partition)
{
    public PartitionInfo Partition { get; } = partition;

    public int Number => Partition.Number;

    public string LetterText => Partition.Letter?.WithColon ?? string.Empty;

    public string Label => Partition.Volume?.Label ?? string.Empty;

    public string TypeName => Partition.Type.Name;

    public string FileSystemText => Partition.Volume is null
        ? (Partition.Type.Family is PartitionFamily.Apple or PartitionFamily.Linux ? "Can't read on Windows" : string.Empty)
        : Partition.FileSystem.DisplayName();

    public string SizeText => Partition.Size.ToString();

    public string FreeText => Partition.Volume is { FileSystem: not (FileSystemKind.None or FileSystemKind.Unknown) } volume
        ? volume.FreeSpace.ToString()
        : string.Empty;

    public string Notes
    {
        get
        {
            var notes = new List<string>();
            if (Partition.IsBoot) notes.Add("Windows");
            if (Partition.IsSystem) notes.Add("Boot files");
            if (Partition.HoldsPagefile) notes.Add("Page file");
            if (Partition.HostsApplication) notes.Add("Diskpart+ runs here");
            if (Partition.Volume?.IsBitLockerProtected == true) notes.Add("BitLocker");
            if (Partition.IsHidden) notes.Add("Hidden");
            if (Partition.Type.Notes is { } typeNote) notes.Add(typeNote);
            return string.Join(" · ", notes);
        }
    }
}

public sealed class SegmentViewModel
{
    private SegmentViewModel()
    {
    }

    public PartitionViewModel? Partition { get; private init; }

    public string Label { get; private init; } = string.Empty;

    public string Detail { get; private init; } = string.Empty;

    public string ToolTip { get; private init; } = string.Empty;

    public double Weight { get; private init; }

    public Brush Fill { get; private init; } = Palette.Unknown;

    public Brush TextBrush { get; private init; } = Palette.White;

    public Brush Stroke { get; private init; } = Brushes.Transparent;

    public Thickness StrokeThickness { get; private init; }

    public static SegmentViewModel For(PartitionViewModel partition)
    {
        var p = partition.Partition;
        var name = string.IsNullOrEmpty(partition.Label) ? p.Type.ShortName ?? p.Type.Name : partition.Label;
        var label = p.Letter is { } letter ? $"{letter.WithColon} {name}" : name;
        var detail = string.IsNullOrEmpty(partition.FileSystemText) ? p.Size.ToString() : $"{p.Size} {partition.FileSystemText}";

        return new SegmentViewModel
        {
            Partition = partition,
            Label = label,
            Detail = detail,
            ToolTip = $"Partition {p.Number}: {label}\n{p.Type.Name}\n{detail}",
            Weight = p.Size.Bytes,
            Fill = Palette.ForPartition(p.Type),
        };
    }

    public static SegmentViewModel Free(ByteSize size, string label = "Unallocated") => new()
    {
        Label = label,
        Detail = size.ToString(),
        ToolTip = $"{label}\n{size}",
        Weight = size.Bytes,
        Fill = Palette.FreeFill,
        TextBrush = Palette.FreeText,
        Stroke = Palette.FreeStroke,
        StrokeThickness = new Thickness(1),
    };
}
