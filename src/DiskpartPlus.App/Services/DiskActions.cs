using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using DiskpartPlus.App.Views;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Templates;

namespace DiskpartPlus.App.Services;

/// <summary>
/// Asks the user for whatever an action needs and turns the answers into operations.
/// Nothing here decides whether something is safe. That's the planner's job, and the
/// review window shows its verdict before anything runs.
/// Every method returns null if the user cancels.
/// </summary>
internal static class DiskActions
{
    private sealed record LetterChoice(DriveLetter? Letter, bool None);

    public static IReadOnlyList<DiskOperation>? WipeAndReuse(Window owner, StorageSnapshot snapshot, DiskInfo disk)
    {
        var form = new FormDialog(owner, "Wipe and reuse",
            $"Removes everything on {disk.DisplayName} and makes one partition that fills the whole disk. Handy for drives that came from a Mac or had Linux on them.");

        var style = form.AddChoice("Partition style", StyleChoices(), help: "GPT suits almost everything. Pick MBR for old PCs, some TVs and car stereos.");
        var fileSystem = form.AddChoice("File system", FileSystemChoices(includeRefs: false));
        var label = form.AddText("Label", "DATA");
        var letter = form.AddChoice("Drive letter", LetterChoices(snapshot, includeNone: true));
        var zero = form.AddCheck("Overwrite every sector (clean all)", false, "Much slower, sometimes hours. Only needed if you're giving the drive away.");

        if (!form.Ask())
        {
            return null;
        }

        return Core.Templates.WipeAndReuse.Build(disk.Number, new WipeAndReuseOptions
        {
            Style = FormDialog.ValueOf<PartitionStyle>(style),
            FileSystem = FormDialog.ValueOf<FileSystemKind>(fileSystem),
            Label = label.Text.Trim(),
            Assign = ToAssignment(FormDialog.ValueOf<LetterChoice>(letter)),
            ZeroEverySector = zero.IsChecked == true,
        });
    }

    public static IReadOnlyList<DiskOperation>? CreatePartition(Window owner, StorageSnapshot snapshot, DiskInfo disk)
    {
        var form = new FormDialog(owner, "Create partition", $"Adds a new partition to {disk.DisplayName}.");

        var kinds = new List<(string, NewPartitionKind)> { ("Normal partition", NewPartitionKind.Primary) };
        if (disk.Style == PartitionStyle.Gpt)
        {
            kinds.Add(("EFI system partition", NewPartitionKind.Efi));
            kinds.Add(("Microsoft reserved partition", NewPartitionKind.MicrosoftReserved));
        }

        var kind = form.AddChoice("Type", kinds);
        var size = form.AddText("Size in MB", string.Empty, $"Leave empty to use all of the free space ({disk.LargestFreeExtent}).");
        var fileSystem = form.AddChoice("File system", NoneOr(FileSystemChoices(includeRefs: true)), selectedIndex: 1);
        var label = form.AddText("Label", "DATA");
        var letter = form.AddChoice("Drive letter", LetterChoices(snapshot, includeNone: true));
        form.Validate = () => OptionalMegabytes(size.Text, out _) ? null : "Size has to be a whole number of MB, or empty.";

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
        var form = new FormDialog(owner, "Convert partition style", $"Switches {disk.DisplayName} between GPT and MBR. diskpart can only do this on an empty disk.");
        var target = form.AddChoice("Convert to", StyleChoices(), selectedIndex: disk.Style == PartitionStyle.Gpt ? 1 : 0);
        var clean = form.AddCheck("Remove all partitions first (clean)", disk.Partitions.Count > 0, "Needed if the disk has anything on it. Everything on it will be lost.");

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

        var form = new FormDialog(owner, "Change disk ID",
            "Windows won't keep two disks with the same ID online at once. That usually happens after cloning a drive. Giving one of them a new ID fixes it.");
        var id = form.AddText("New ID", suggested, disk.Style == PartitionStyle.Mbr ? "8 hex digits." : "A GUID.");

        return form.Ask() ? [new SetDiskUniqueId(disk.Number, id.Text.Trim())] : null;
    }

    public static IReadOnlyList<DiskOperation>? Format(Window owner, PartitionInfo partition)
    {
        var form = new FormDialog(owner, "Format partition", $"Formats partition {partition.Number} on disk {partition.DiskNumber}. Everything on it will be lost.");
        var fileSystem = form.AddChoice("File system", FileSystemChoices(includeRefs: true), selectedIndex: partition.Size > ByteSize.FromGigabytes(32) ? 1 : 0);
        var label = form.AddText("Label", partition.Volume?.Label ?? "DATA");
        var unit = form.AddChoice("Allocation unit size", new (string, int?)[] { ("Default", null), ("4 KB", 4096), ("64 KB", 65536) });
        var quick = form.AddCheck("Quick format", true, "A full format checks every sector and takes much longer.");

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
        var title = partition.Letter is { } current ? $"Change {current.WithColon}" : "Give it a drive letter";
        var form = new FormDialog(owner, title, $"Partition {partition.Number} on disk {partition.DiskNumber}.");
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
        var form = new FormDialog(owner, "Extend partition", "Grows the partition into free space straight after it. Only works on NTFS and ReFS.");
        var size = form.AddText("Grow by (MB)", string.Empty, "Leave empty to use all of the free space after it.");
        form.Validate = () => OptionalMegabytes(size.Text, out _) ? null : "Size has to be a whole number of MB, or empty.";

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
        var form = new FormDialog(owner, "Shrink partition", "Makes the partition smaller and leaves free space after it. Only works on NTFS. Your files stay put.");
        var size = form.AddText("Shrink by (MB)", "1024", free is { } f ? $"There's {f} free on it right now." : null);
        form.Validate = () => OptionalMegabytes(size.Text, out var mb) && mb is > 0 ? null : "Enter how many MB to shrink by.";

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

        var form = new FormDialog(owner, "Change partition type", "Changes what the partition is marked as. The data on it isn't touched, but the wrong type can hide it or stop a PC booting.");
        var choice = form.AddChoice("New type", types.Select(t => (t.Name, t)), selectedIndex: Math.Max(0, types.IndexOf(partition.Type)));

        return form.Ask() ? [new SetPartitionType(disk.Number, partition.Number, FormDialog.ValueOf<PartitionType>(choice))] : null;
    }

    public static IReadOnlyList<DiskOperation>? GptAttributes(Window owner, PartitionInfo partition)
    {
        var current = partition.GptAttributes;
        var form = new FormDialog(owner, "GPT attributes", "Low-level flags on a GPT partition. Most people never need these.");
        var noLetter = form.AddCheck("Don't give it a drive letter automatically", current.HasFlag(GptAttributeFlags.NoDriveLetter));
        var hidden = form.AddCheck("Hidden", current.HasFlag(GptAttributeFlags.Hidden));
        var readOnly = form.AddCheck("Read-only", current.HasFlag(GptAttributeFlags.ReadOnly));
        var shadow = form.AddCheck("Shadow copy", current.HasFlag(GptAttributeFlags.ShadowCopy));
        var required = form.AddCheck("Required by the platform", false, "Recovery partitions usually have this and \"don't give it a letter\" set.");

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
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Virtual disk.vhdx");
        var form = new FormDialog(owner, "Create virtual disk", "Makes a .vhdx or .vhd file that Windows treats like a real disk. Great for testing.", "Continue");
        var path = form.AddPath("File", defaultPath, save: true, "Virtual disk (*.vhdx)|*.vhdx|Older virtual disk (*.vhd)|*.vhd", ".vhdx");
        var size = form.AddText("Maximum size in GB", "20");
        var allocation = form.AddChoice("Space on disk", new[] { ("Grows as you fill it (expandable)", VirtualDiskAllocation.Expandable), ("Full size straight away (fixed)", VirtualDiskAllocation.Fixed) });
        var setup = form.AddCheck("Set it up with one partition", true);
        var style = form.AddChoice("Partition style", StyleChoices());
        var fileSystem = form.AddChoice("File system", FileSystemChoices(includeRefs: true), selectedIndex: 1);
        var label = form.AddText("Label", "VHD");
        var letter = form.AddChoice("Drive letter", LetterChoices(snapshot, includeNone: true));

        form.Validate = () => long.TryParse(size.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var gb) && gb > 0
            ? null
            : "Size has to be a whole number of GB.";

        if (!form.Ask())
        {
            return null;
        }

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
        var form = new FormDialog(owner, "Attach virtual disk", "Mounts a .vhdx or .vhd file so it shows up as a disk.");
        var path = form.AddPath("File", string.Empty, save: false, "Virtual disks (*.vhdx;*.vhd)|*.vhdx;*.vhd", ".vhdx");
        var readOnly = form.AddCheck("Read-only", false, "Nothing can be written to it while it's attached.");

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
            ("exFAT (works on Windows and Mac)", FileSystemKind.ExFat),
            ("NTFS (Windows)", FileSystemKind.Ntfs),
            ("FAT32 (oldest devices, 32 GB max)", FileSystemKind.Fat32),
        };

        if (includeRefs)
        {
            choices.Add(("ReFS (some Windows editions only)", FileSystemKind.Refs));
        }

        return choices;
    }

    private static IEnumerable<(string, FileSystemKind?)> NoneOr(IEnumerable<(string Text, FileSystemKind Value)> choices) =>
        new (string, FileSystemKind?)[] { ("Don't format it", null) }.Concat(choices.Select(c => (c.Text, (FileSystemKind?)c.Value)));

    private static List<(string, LetterChoice)> LetterChoices(StorageSnapshot snapshot, bool includeNone)
    {
        var choices = new List<(string, LetterChoice)> { ("Next free letter", new LetterChoice(null, false)) };
        if (includeNone)
        {
            choices.Add(("No letter", new LetterChoice(null, true)));
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
