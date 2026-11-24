using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows;
using DiskpartPlus.App.Views;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Templates;

namespace DiskpartPlus.App.Services;

/// <summary>
/// Asks for whatever an action needs and turns the answers into operations.
/// Safety is the planner's job, not this one. Every method returns null on cancel.
/// </summary>
internal static class DiskActions
{
    private sealed record LetterChoice(DriveLetter? Letter, bool None);

    public static IReadOnlyList<DiskOperation>? WipeAndReuse(Window owner, StorageSnapshot snapshot, DiskInfo disk)
    {
        var (form, read) = WipeAndReuseForm(owner, snapshot, disk);
        return form.Ask() ? read() : null;
    }

    // Split out so the UI check in CI can open the form without waiting for someone to click.
    internal static (FormDialog Form, Func<IReadOnlyList<DiskOperation>> Read) WipeAndReuseForm(Window owner, StorageSnapshot snapshot, DiskInfo disk)
    {
        var form = new FormDialog(owner, "Wipe and reuse", disk.DisplayName);

        var style = form.AddChoice("Partition style", StyleChoices());
        var fileSystem = form.AddChoice("File system", FileSystemChoices(includeRefs: false));
        var label = form.AddText("Label", "DATA");
        var letter = form.AddChoice("Drive letter", LetterChoices(snapshot, includeNone: true));
        var zero = form.AddCheck("Zero every sector (clean all, slow)", false);

        return (form, () => Core.Templates.WipeAndReuse.Build(disk.Number, new WipeAndReuseOptions
        {
            Style = FormDialog.ValueOf<PartitionStyle>(style),
            FileSystem = FormDialog.ValueOf<FileSystemKind>(fileSystem),
            Label = label.Text.Trim(),
            Assign = ToAssignment(FormDialog.ValueOf<LetterChoice>(letter)),
            ZeroEverySector = zero.IsChecked == true,
        }));
    }

    public static IReadOnlyList<DiskOperation>? CreatePartition(Window owner, StorageSnapshot snapshot, DiskInfo disk)
    {
        var form = new FormDialog(owner, "Create partition", disk.DisplayName);

        var kinds = new List<(string, NewPartitionKind)> { ("Primary", NewPartitionKind.Primary) };
        if (disk.Style == PartitionStyle.Gpt)
        {
            kinds.Add(("EFI", NewPartitionKind.Efi));
            kinds.Add(("MSR", NewPartitionKind.MicrosoftReserved));
        }

        var kind = form.AddChoice("Type", kinds);
        var size = form.AddText("Size (MB)", string.Empty, $"Empty = all free space ({disk.LargestFreeExtent})");
        var fileSystem = form.AddChoice("File system", NoneOr(FileSystemChoices(includeRefs: true)), selectedIndex: 1);
        var label = form.AddText("Label", "DATA");
        var letter = form.AddChoice("Drive letter", LetterChoices(snapshot, includeNone: true));
        form.Validate = () => OptionalMegabytes(size.Text, out _) ? null : "Size must be a whole number of MB.";

        if (!form.Ask())
        {
            return null;
        }

        OptionalMegabytes(size.Text, out var megabytes);
        var chosenFileSystem = FormDialog.ValueOf<FileSystemKind?>(fileSystem);

        return
        [
            new CreatePartition(
                disk.Number,
                FormDialog.ValueOf<NewPartitionKind>(kind),
                megabytes is { } mb ? ByteSize.FromMegabytes(mb) : null,
                chosenFileSystem is { } fs ? new FormatSpec(fs, label.Text.Trim()) : null,
                ToAssignment(FormDialog.ValueOf<LetterChoice>(letter))),
        ];
    }

    public static IReadOnlyList<DiskOperation>? ConvertStyle(Window owner, DiskInfo disk)
    {
        var form = new FormDialog(owner, "Convert", disk.DisplayName);
        var target = form.AddChoice("Convert to", StyleChoices(), selectedIndex: disk.Style == PartitionStyle.Gpt ? 1 : 0);
        var clean = form.AddCheck("Clean first", disk.Partitions.Count > 0);

        if (!form.Ask())
        {
            return null;
        }

        var operations = new List<DiskOperation>();
        if (clean.IsChecked == true)
        {
            operations.Add(new CleanDisk(disk.Number));
        }

        operations.Add(new ConvertPartitionStyle(disk.Number, FormDialog.ValueOf<PartitionStyle>(target)));
        return operations;
    }

    public static IReadOnlyList<DiskOperation>? ChangeDiskId(Window owner, DiskInfo disk)
    {
        var suggested = disk.Style == PartitionStyle.Mbr
            ? RandomNumberGenerator.GetInt32(0x10000000, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture)
            : Guid.NewGuid().ToString("D").ToUpperInvariant();

        var form = new FormDialog(owner, "Change disk ID", disk.DisplayName);
        var id = form.AddText(disk.Style == PartitionStyle.Mbr ? "New ID (8 hex digits)" : "New ID (GUID)", suggested);

        return form.Ask() ? [new SetDiskUniqueId(disk.Number, id.Text.Trim())] : null;
    }

    public static IReadOnlyList<DiskOperation>? Format(Window owner, PartitionInfo partition)
    {
        var form = new FormDialog(owner, "Format", $"Disk {partition.DiskNumber}, partition {partition.Number}");
        var fileSystem = form.AddChoice("File system", FileSystemChoices(includeRefs: true), selectedIndex: partition.Size > ByteSize.FromGigabytes(32) ? 1 : 0);
        var label = form.AddText("Label", partition.Volume?.Label ?? "DATA");
        var unit = form.AddChoice("Allocation unit", new (string, int?)[] { ("Default", null), ("4 KB", 4096), ("64 KB", 65536) });
        var quick = form.AddCheck("Quick format", true);

        if (!form.Ask())
        {
            return null;
        }

        var spec = new FormatSpec(FormDialog.ValueOf<FileSystemKind>(fileSystem), label.Text.Trim(), quick.IsChecked == true, FormDialog.ValueOf<int?>(unit));
        return [new FormatPartition(partition.DiskNumber, partition.Number, spec)];
    }

    public static IReadOnlyList<DiskOperation> Delete(PartitionInfo partition) =>
        [new DeletePartition(partition.DiskNumber, partition.Number, Override: partition.Type.NeedsOverrideToDelete)];

    public static IReadOnlyList<DiskOperation>? ChangeLetter(Window owner, StorageSnapshot snapshot, PartitionInfo partition)
    {
        var title = partition.Letter is { } current ? $"Change {current.WithColon}" : "Drive letter";
        var form = new FormDialog(owner, title, $"Disk {partition.DiskNumber}, partition {partition.Number}");
        var letter = form.AddChoice("Drive letter", LetterChoices(snapshot, includeNone: false));

        if (!form.Ask())
        {
            return null;
        }

        var choice = FormDialog.ValueOf<LetterChoice>(letter);
        return [new AssignDriveLetter(partition.DiskNumber, partition.Number, choice.Letter, Replacing: partition.Letter)];
    }

    public static IReadOnlyList<DiskOperation>? RemoveLetter(PartitionInfo partition) =>
        partition.Letter is { } letter ? [new RemoveDriveLetter(partition.DiskNumber, partition.Number, letter)] : null;

    public static IReadOnlyList<DiskOperation>? Extend(Window owner, PartitionInfo partition)
    {
        var form = new FormDialog(owner, "Extend", $"Disk {partition.DiskNumber}, partition {partition.Number}");
        var size = form.AddText("Grow by (MB)", string.Empty, "Empty = all free space after it");
        form.Validate = () => OptionalMegabytes(size.Text, out _) ? null : "Size must be a whole number of MB.";

        if (!form.Ask())
        {
            return null;
        }

        OptionalMegabytes(size.Text, out var megabytes);
        return [new ExtendPartition(partition.DiskNumber, partition.Number, megabytes is { } mb ? ByteSize.FromMegabytes(mb) : null)];
    }

    public static IReadOnlyList<DiskOperation>? Shrink(Window owner, PartitionInfo partition)
    {
        var free = partition.Volume?.FreeSpace;
        var form = new FormDialog(owner, "Shrink", $"Disk {partition.DiskNumber}, partition {partition.Number}");
        var size = form.AddText("Shrink by (MB)", "1024", free is { } f ? $"{f} free" : null);
        form.Validate = () => OptionalMegabytes(size.Text, out var mb) && mb is > 0 ? null : "Size must be a whole number of MB.";

        if (!form.Ask())
        {
            return null;
        }

        OptionalMegabytes(size.Text, out var megabytes);
        return [new ShrinkPartition(partition.DiskNumber, partition.Number, ByteSize.FromMegabytes(megabytes ?? 0))];
    }

    public static IReadOnlyList<DiskOperation>? ChangeType(Window owner, DiskInfo disk, PartitionInfo partition)
    {
        var types = PartitionTypes.All
            .Where(t => disk.Style == PartitionStyle.Gpt ? t.GptId is not null : t.MbrId is not null)
            .ToList();

        var form = new FormDialog(owner, "Change type", $"Disk {disk.Number}, partition {partition.Number}");
        var choice = form.AddChoice("New type", types.Select(t => (t.Name, t)), selectedIndex: Math.Max(0, types.IndexOf(partition.Type)));

        return form.Ask() ? [new SetPartitionType(disk.Number, partition.Number, FormDialog.ValueOf<PartitionType>(choice))] : null;
    }

    public static IReadOnlyList<DiskOperation>? GptAttributes(Window owner, PartitionInfo partition)
    {
        var current = partition.GptAttributes;
        var form = new FormDialog(owner, "GPT attributes", $"Disk {partition.DiskNumber}, partition {partition.Number}");
        var noLetter = form.AddCheck("No default drive letter", current.HasFlag(GptAttributeFlags.NoDriveLetter));
        var hidden = form.AddCheck("Hidden", current.HasFlag(GptAttributeFlags.Hidden));
        var readOnly = form.AddCheck("Read-only", current.HasFlag(GptAttributeFlags.ReadOnly));
        var shadow = form.AddCheck("Shadow copy", current.HasFlag(GptAttributeFlags.ShadowCopy));
        var required = form.AddCheck("Required (platform)", false);

        if (!form.Ask())
        {
            return null;
        }

        var flags = GptAttributeFlags.None;
        if (noLetter.IsChecked == true) flags |= GptAttributeFlags.NoDriveLetter;
        if (hidden.IsChecked == true) flags |= GptAttributeFlags.Hidden;
        if (readOnly.IsChecked == true) flags |= GptAttributeFlags.ReadOnly;
        if (shadow.IsChecked == true) flags |= GptAttributeFlags.ShadowCopy;
        if (required.IsChecked == true) flags |= GptAttributeFlags.RequiredPartition;

        return [new SetGptAttributes(partition.DiskNumber, partition.Number, flags)];
    }

    public static IReadOnlyList<DiskOperation>? CreateVirtualDisk(Window owner, StorageSnapshot snapshot)
    {
        var (form, read) = CreateVirtualDiskForm(owner, snapshot);
        return form.Ask() ? read() : null;
    }

    internal static (FormDialog Form, Func<IReadOnlyList<DiskOperation>> Read) CreateVirtualDiskForm(Window owner, StorageSnapshot snapshot)
    {
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Virtual disk.vhdx");
        var form = new FormDialog(owner, "Create virtual disk");
        var path = form.AddPath("File", defaultPath, save: true, "VHDX (*.vhdx)|*.vhdx|VHD (*.vhd)|*.vhd", ".vhdx");
        var size = form.AddText("Size (GB)", "20");
        var allocation = form.AddChoice("Type", new[] { ("Expandable", VirtualDiskAllocation.Expandable), ("Fixed", VirtualDiskAllocation.Fixed) });
        var setup = form.AddCheck("Create and format a partition", true);
        var style = form.AddChoice("Partition style", StyleChoices());
        var fileSystem = form.AddChoice("File system", FileSystemChoices(includeRefs: true), selectedIndex: 1);
        var label = form.AddText("Label", "VHD");
        var letter = form.AddChoice("Drive letter", LetterChoices(snapshot, includeNone: true));

        form.Validate = () => long.TryParse(size.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var gb) && gb > 0
            ? null
            : "Size must be a whole number of GB.";

        return (form, () => ReadVirtualDiskForm(path, size, allocation, setup, style, fileSystem, label, letter));
    }

    private static IReadOnlyList<DiskOperation> ReadVirtualDiskForm(
        TextBox path, TextBox size, ComboBox allocation, CheckBox setup, ComboBox style, ComboBox fileSystem, TextBox label, ComboBox letter)
    {
        var gigabytes = long.Parse(size.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
        VirtualDiskSetup? diskSetup = setup.IsChecked == true
            ? new VirtualDiskSetup(
                FormDialog.ValueOf<PartitionStyle>(style),
                new FormatSpec(FormDialog.ValueOf<FileSystemKind>(fileSystem), label.Text.Trim()),
                ToAssignment(FormDialog.ValueOf<LetterChoice>(letter)))
            : null;

        return
        [
            new Core.Operations.CreateVirtualDisk(
                path.Text.Trim(),
                ByteSize.FromGigabytes(gigabytes),
                FormDialog.ValueOf<VirtualDiskAllocation>(allocation),
                Attach: true,
                Setup: diskSetup),
        ];
    }

    public static IReadOnlyList<DiskOperation>? AttachVirtualDisk(Window owner)
    {
        var form = new FormDialog(owner, "Attach virtual disk");
        var path = form.AddPath("File", string.Empty, save: false, "Virtual disks (*.vhdx;*.vhd)|*.vhdx;*.vhd", ".vhdx");
        var readOnly = form.AddCheck("Read-only", false);

        return form.Ask() ? [new Core.Operations.AttachVirtualDisk(path.Text.Trim(), readOnly.IsChecked == true)] : null;
    }

    public static IReadOnlyList<DiskOperation>? DetachVirtualDisk(DiskInfo disk) =>
        disk.Location is { } location ? [new Core.Operations.DetachVirtualDisk(location)] : null;

    private static (string, PartitionStyle)[] StyleChoices() =>
    [
        ("GPT", PartitionStyle.Gpt),
        ("MBR", PartitionStyle.Mbr),
    ];

    private static List<(string, FileSystemKind)> FileSystemChoices(bool includeRefs)
    {
        var choices = new List<(string, FileSystemKind)>
        {
            ("exFAT", FileSystemKind.ExFat),
            ("NTFS", FileSystemKind.Ntfs),
            ("FAT32", FileSystemKind.Fat32),
        };

        if (includeRefs)
        {
            choices.Add(("ReFS", FileSystemKind.Refs));
        }

        return choices;
    }

    private static IEnumerable<(string, FileSystemKind?)> NoneOr(IEnumerable<(string Text, FileSystemKind Value)> choices) =>
        new (string, FileSystemKind?)[] { ("None", null) }.Concat(choices.Select(c => (c.Text, (FileSystemKind?)c.Value)));

    private static List<(string, LetterChoice)> LetterChoices(StorageSnapshot snapshot, bool includeNone)
    {
        var choices = new List<(string, LetterChoice)> { ("Next free", new LetterChoice(null, false)) };
        if (includeNone)
        {
            choices.Add(("None", new LetterChoice(null, true)));
        }

        choices.AddRange(snapshot.AvailableLetters().Select(l => (l.WithColon, new LetterChoice(l, false))));
        return choices;
    }

    private static DriveAssignment? ToAssignment(LetterChoice choice) =>
        choice.None ? null : new DriveAssignment(choice.Letter);

    private static bool OptionalMegabytes(string text, out long? megabytes)
    {
        megabytes = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            megabytes = value;
            return true;
        }

        return false;
    }
}
