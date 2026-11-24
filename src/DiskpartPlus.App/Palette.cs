using System.Windows.Media;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Safety;

namespace DiskpartPlus.App;

/// <summary>
/// The app's colours. Picked to read well on both the light and dark theme.
/// </summary>
internal static class Palette
{
    public static readonly Brush WindowsData = Make(0x25, 0x63, 0xEB);
    public static readonly Brush WindowsSystem = Make(0x47, 0x55, 0x69);
    public static readonly Brush Apple = Make(0x93, 0x33, 0xEA);
    public static readonly Brush Linux = Make(0xEA, 0x58, 0x0C);
    public static readonly Brush Unknown = Make(0x64, 0x74, 0x8B);
    public static readonly Brush FreeFill = Make(0x94, 0xA3, 0xB8, alpha: 0x26);
    public static readonly Brush FreeStroke = Make(0x94, 0xA3, 0xB8);
    public static readonly Brush FreeText = Make(0x64, 0x74, 0x8B);
    public static readonly Brush White = Make(0xFF, 0xFF, 0xFF);

    public static readonly Brush Danger = Make(0xDC, 0x26, 0x26);
    public static readonly Brush Caution = Make(0xD9, 0x77, 0x06);
    public static readonly Brush Good = Make(0x16, 0xA3, 0x4A);
    public static readonly Brush Info = Make(0x25, 0x63, 0xEB);
    public static readonly Brush Muted = Make(0x6B, 0x72, 0x80);
    public static readonly Brush DeepRed = Make(0x99, 0x1B, 0x1B);

    public static Brush ForPartition(PartitionType type)
    {
        if (type.Family == PartitionFamily.Apple)
        {
            return Apple;
        }

        if (type.Family == PartitionFamily.Linux)
        {
            return Linux;
        }

        if (type.IsDataPartition)
        {
            return WindowsData;
        }

        return type.Family == PartitionFamily.Windows ? WindowsSystem : Unknown;
    }

    public static Brush ForDisk(DiskInfo disk) => disk switch
    {
        { IsVirtual: true } => Make(0x8B, 0x5C, 0xF6),
        { Bus: BusType.Usb } => Make(0xF5, 0x9E, 0x0B),
        { Bus: BusType.Nvme } => Make(0x0E, 0xA5, 0xE9),
        { Media: MediaType.Ssd } => Make(0x14, 0xB8, 0xA6),
        { Media: MediaType.Hdd } => Make(0x63, 0x66, 0xF1),
        _ => Unknown,
    };

    public static Brush ForRisk(RiskLevel risk) => risk switch
    {
        RiskLevel.Safe => Good,
        RiskLevel.Caution => Caution,
        RiskLevel.Destructive => Danger,
        _ => DeepRed,
    };

    public static Brush ForSeverity(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Blocker => Danger,
        IssueSeverity.Warning => Caution,
        _ => Info,
    };

    private static SolidColorBrush Make(byte r, byte g, byte b, byte alpha = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        brush.Freeze();
        return brush;
    }
}
