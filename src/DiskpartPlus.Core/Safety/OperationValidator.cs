using System.Globalization;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Partitions;
using DiskpartPlus.Core.Planning;

namespace DiskpartPlus.Core.Safety;

/// <summary>
/// Catches things diskpart would reject, so the user gets one clear line up front instead of an
/// error halfway through a script. Only blockers, no nagging.
/// </summary>
internal static class OperationValidator
{
    private static readonly ByteSize Fat32FormatLimit = ByteSize.FromGigabytes(32);
    private static readonly ByteSize MinimumVirtualDisk = ByteSize.FromMegabytes(3);
    private static readonly ByteSize MaximumVhd = ByteSize.FromGigabytes(2040);
    private static readonly ByteSize MaximumVhdx = ByteSize.FromTerabytes(64);

    public static void Check(DiskOperation operation, PlanState state, List<PlanIssue> issues)
    {
        switch (operation)
        {
            case CleanDisk clean:
                CheckWritableDisk(clean, state, issues);
                break;

            case ConvertPartitionStyle convert:
                CheckConvert(convert, state, issues);
                break;

            case SetDiskOnline online:
                state.ResolveDisk(online, issues);
                break;

            case SetDiskReadOnly readOnly:
                CheckOnlineDisk(readOnly, state, issues);
                break;

            case SetDiskUniqueId uniqueId:
                CheckUniqueId(uniqueId, state, issues);
                break;

            case CreatePartition create:
                CheckCreate(create, state, issues);
                break;

            case FormatPartition format:
                CheckFormat(format, state, issues);
                break;

            case DeletePartition delete:
                CheckWritablePartition(delete, state, issues);
                break;

            case ExtendPartition extend:
                CheckExtend(extend, state, issues);
                break;

            case ShrinkPartition shrink:
                CheckShrink(shrink, state, issues);
                break;

            case AssignDriveLetter assign:
                CheckAssign(assign, state, issues);
                break;

            case RemoveDriveLetter remove:
                CheckRemoveLetter(remove, state, issues);
                break;

            case SetPartitionType setType:
                CheckSetType(setType, state, issues);
                break;

            case SetGptAttributes attributes:
                CheckGptAttributes(attributes, state, issues);
                break;

            case CreateVirtualDisk create:
                CheckCreateVirtualDisk(create, state, issues);
                break;

            case AttachVirtualDisk attach:
                CheckAttach(attach, state, issues);
                break;

            case DetachVirtualDisk detach:
                if (CheckPath(detach, state, issues) && state.Snapshot.FindVirtualDisk(detach.FilePath) is null)
                {
                    issues.Add(Blocker(IssueCodes.NotAttached, "Not attached.", detach));
                }

                break;
        }
    }

    private static DiskInfo? CheckOnlineDisk(DiskScopedOperation operation, PlanState state, List<PlanIssue> issues)
    {
        var disk = state.ResolveDisk(operation, issues);
        if (disk is { IsOffline: true })
        {
            issues.Add(Blocker(IssueCodes.DiskOffline, $"Disk {disk.Number} is offline.", operation));
        }

        return disk;
    }

    private static DiskInfo? CheckWritableDisk(DiskScopedOperation operation, PlanState state, List<PlanIssue> issues)
    {
        var disk = CheckOnlineDisk(operation, state, issues);
        if (disk is { IsReadOnly: true })
        {
            issues.Add(Blocker(IssueCodes.DiskReadOnly, $"Disk {disk.Number} is read-only.", operation));
        }

        return disk;
    }

    private static PartitionInfo? CheckWritablePartition(PartitionScopedOperation operation, PlanState state, List<PlanIssue> issues)
    {
        var disk = CheckWritableDisk(operation, state, issues);
        return disk is null ? null : state.ResolvePartition(operation, issues);
    }

    private static void CheckConvert(ConvertPartitionStyle convert, PlanState state, List<PlanIssue> issues)
    {
        if (convert.To is not (PartitionStyle.Gpt or PartitionStyle.Mbr))
        {
            issues.Add(Blocker(IssueCodes.InvalidStyle, "Pick GPT or MBR.", convert));
            return;
        }

        var disk = CheckWritableDisk(convert, state, issues);
        if (disk is null)
        {
            return;
        }

        if (!state.IsEmpty(disk))
        {
            issues.Add(Blocker(IssueCodes.DiskNotEmpty,
                $"Disk {disk.Number} has partitions. Clean it first.", convert));
        }
        else if (state.StyleOf(disk) == convert.To)
        {
            issues.Add(Blocker(IssueCodes.AlreadyThatStyle,
                $"Disk {disk.Number} is already {Style(convert.To)}.", convert));
        }
    }

    private static void CheckUniqueId(SetDiskUniqueId uniqueId, PlanState state, List<PlanIssue> issues)
    {
        var disk = CheckWritableDisk(uniqueId, state, issues);
        if (disk is null)
        {
            return;
        }

        var style = state.StyleOf(disk);
        var valid = style switch
        {
            PartitionStyle.Gpt => Guid.TryParse(uniqueId.Id, out _),
            PartitionStyle.Mbr => uniqueId.Id.Length == 8
                && uint.TryParse(uniqueId.Id, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
                && value != 0,
            _ => false,
        };

        if (!valid)
        {
            var expected = style switch
            {
                PartitionStyle.Gpt => "a GUID",
                PartitionStyle.Mbr => "8 hex digits",
                _ => "to be GPT or MBR first",
            };
            issues.Add(Blocker(IssueCodes.InvalidDiskId, $"Disk {disk.Number} needs {expected}.", uniqueId));
        }
    }

    private static void CheckCreate(CreatePartition create, PlanState state, List<PlanIssue> issues)
    {
        var disk = CheckWritableDisk(create, state, issues);
        if (disk is null)
        {
            return;
        }

        var style = state.StyleOf(disk);
        if (create.Kind is NewPartitionKind.Efi or NewPartitionKind.MicrosoftReserved && style != PartitionStyle.Gpt)
        {
            issues.Add(Blocker(IssueCodes.GptOnly, "EFI and MSR partitions need a GPT disk.", create));
        }

        var free = state.FreeSpaceOn(disk);
        if (create.Size is { } size)
        {
            if (size.WholeMegabytes < 1)
            {
                issues.Add(Blocker(IssueCodes.TooSmall, "Minimum is 1 MB.", create));
            }
            else if (size > free)
            {
                issues.Add(Blocker(IssueCodes.NotEnoughSpace, $"Only {free} free.", create));
            }
        }
        else if (free.WholeMegabytes < 1)
        {
            issues.Add(Blocker(IssueCodes.NotEnoughSpace, $"No free space on disk {disk.Number}.", create));
        }

        if (create.Format is not null)
        {
            CheckFormatSpec(create.Format, create.Size ?? free, create, state, issues);
        }

        if (create.Assign?.Letter is { } letter && state.IsLetterTaken(letter))
        {
            issues.Add(Blocker(IssueCodes.LetterInUse, $"{letter.WithColon} is taken.", create));
        }
    }

    private static void CheckFormat(FormatPartition format, PlanState state, List<PlanIssue> issues)
    {
        var partition = CheckWritablePartition(format, state, issues);
        if (partition is null)
        {
            return;
        }

        CheckFormatSpec(format.Format, partition.Size, format, state, issues);
    }

    private static void CheckFormatSpec(FormatSpec spec, ByteSize size, DiskOperation operation, PlanState state, List<PlanIssue> issues)
    {
        var fs = spec.FileSystem;
        if (!fs.CanFormatTo())
        {
            issues.Add(Blocker(IssueCodes.CantFormatTo, "Can't format to that.", operation));
            return;
        }

        if (spec.Label.Length > fs.MaxLabelLength())
        {
            issues.Add(Blocker(IssueCodes.LabelTooLong,
                $"{fs.DisplayName()} labels are {fs.MaxLabelLength()} characters max.", operation));
        }

        if (fs.FindBadLabelChar(spec.Label) is { } bad)
        {
            var shown = char.IsControl(bad) ? "a control character" : $"\"{bad}\"";
            issues.Add(Blocker(IssueCodes.LabelBadCharacter, $"{fs.DisplayName()} labels can't contain {shown}.", operation));
        }
        else if (!state.Context.ScriptCanHold(spec.Label))
        {
            issues.Add(Blocker(IssueCodes.UnsupportedCharacters,
                "diskpart can't handle some characters in that label on this PC.", operation));
        }

        if (fs == FileSystemKind.Fat32 && size > Fat32FormatLimit)
        {
            issues.Add(Blocker(IssueCodes.Fat32TooLarge,
                "Windows won't format FAT32 over 32 GB. Use exFAT.", operation));
        }
    }

    private static void CheckExtend(ExtendPartition extend, PlanState state, List<PlanIssue> issues)
    {
        var partition = CheckWritablePartition(extend, state, issues);
        if (partition is null || state.Snapshot.FindDisk(extend.DiskNumber) is not { } disk)
        {
            return;
        }

        if (partition.FileSystem is not (FileSystemKind.Ntfs or FileSystemKind.Refs or FileSystemKind.None))
        {
            issues.Add(Blocker(IssueCodes.UnsupportedFileSystem,
                "Only NTFS and ReFS can be extended.", extend));
        }

        // The free space has to come straight after the partition.
        var next = disk.Partitions.Where(p => p.Offset > partition.Offset).Select(p => p.Offset).DefaultIfEmpty(disk.Size).Min();
        var room = ByteSize.Max(next - partition.End, ByteSize.Zero);

        if (room.WholeMegabytes < 1)
        {
            issues.Add(Blocker(IssueCodes.NoSpaceAfter, "No free space right after this partition.", extend));
        }
        else if (extend.By is { } by && by > room)
        {
            issues.Add(Blocker(IssueCodes.NotEnoughSpace, $"Only {room} free after it.", extend));
        }
    }

    private static void CheckShrink(ShrinkPartition shrink, PlanState state, List<PlanIssue> issues)
    {
        var partition = CheckWritablePartition(shrink, state, issues);
        if (partition is null)
        {
            return;
        }

        if (partition.FileSystem != FileSystemKind.Ntfs)
        {
            issues.Add(Blocker(IssueCodes.UnsupportedFileSystem,
                "Only NTFS can be shrunk.", shrink));
            return;
        }

        if (shrink.By.WholeMegabytes < 1)
        {
            issues.Add(Blocker(IssueCodes.TooSmall, "Minimum is 1 MB.", shrink));
        }
        else if (partition.Volume is { } volume && shrink.By >= volume.FreeSpace)
        {
            issues.Add(Blocker(IssueCodes.NotEnoughSpace,
                $"Only {volume.FreeSpace} free on it.", shrink));
        }
    }

    private static void CheckAssign(AssignDriveLetter assign, PlanState state, List<PlanIssue> issues)
    {
        var partition = CheckWritablePartition(assign, state, issues);
        if (partition is null)
        {
            return;
        }

        if (assign.Replacing is { } old)
        {
            if (partition.Letter != old)
            {
                issues.Add(Blocker(IssueCodes.LetterMismatch, $"It doesn't have {old.WithColon}.", assign));
            }
        }
        else if (partition.Letter is { } existing)
        {
            issues.Add(Blocker(IssueCodes.AlreadyHasLetter,
                $"Already has {existing.WithColon}.", assign));
        }

        if (assign.Letter is { } letter && letter != assign.Replacing && state.IsLetterTaken(letter))
        {
            issues.Add(Blocker(IssueCodes.LetterInUse, $"{letter.WithColon} is taken.", assign));
        }
    }

    private static void CheckRemoveLetter(RemoveDriveLetter remove, PlanState state, List<PlanIssue> issues)
    {
        var partition = CheckWritablePartition(remove, state, issues);
        if (partition is not null && partition.Letter != remove.Letter)
        {
            issues.Add(Blocker(IssueCodes.LetterMismatch, $"It doesn't have {remove.Letter.WithColon}.", remove));
        }
    }

    private static void CheckSetType(SetPartitionType setType, PlanState state, List<PlanIssue> issues)
    {
        var partition = CheckWritablePartition(setType, state, issues);
        if (partition is null || state.Snapshot.FindDisk(setType.DiskNumber) is not { } disk)
        {
            return;
        }

        var style = state.StyleOf(disk);
        if (setType.Type.DiskpartId(style) is null)
        {
            issues.Add(Blocker(IssueCodes.TypeDoesNotFitDisk,
                $"{setType.Type.Name} isn't valid on a {Style(style)} disk.", setType));
        }
    }

    private static void CheckGptAttributes(SetGptAttributes attributes, PlanState state, List<PlanIssue> issues)
    {
        var partition = CheckWritablePartition(attributes, state, issues);
        if (partition is not null && state.Snapshot.FindDisk(attributes.DiskNumber) is { } disk && state.StyleOf(disk) != PartitionStyle.Gpt)
        {
            issues.Add(Blocker(IssueCodes.GptOnly, "GPT disks only.", attributes));
        }
    }

    private static void CheckCreateVirtualDisk(CreateVirtualDisk create, PlanState state, List<PlanIssue> issues)
    {
        if (!CheckPath(create, state, issues))
        {
            return;
        }

        if (state.Context.FileExists(create.FilePath))
        {
            issues.Add(Blocker(IssueCodes.FileExists,
                $"{create.FilePath} already exists.", create));
        }

        var isVhdx = create.FilePath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);
        var limit = isVhdx ? MaximumVhdx : MaximumVhd;

        if (create.MaximumSize < MinimumVirtualDisk)
        {
            issues.Add(Blocker(IssueCodes.TooSmall, "Minimum is 3 MB.", create));
        }
        else if (create.MaximumSize > limit)
        {
            issues.Add(Blocker(IssueCodes.VirtualDiskTooBig,
                isVhdx ? "VHDX max is 64 TB." : "VHD max is 2040 GB. Use .vhdx.", create));
        }

        if (create.Setup is { } setup)
        {
            if (setup.Style is not (PartitionStyle.Gpt or PartitionStyle.Mbr))
            {
                issues.Add(Blocker(IssueCodes.InvalidStyle, "Pick GPT or MBR.", create));
            }

            CheckFormatSpec(setup.Format, create.MaximumSize, create, state, issues);

            if (setup.Assign?.Letter is { } letter && state.IsLetterTaken(letter))
            {
                issues.Add(Blocker(IssueCodes.LetterInUse, $"{letter.WithColon} is taken.", create));
            }
        }
    }

    private static void CheckAttach(AttachVirtualDisk attach, PlanState state, List<PlanIssue> issues)
    {
        if (!CheckPath(attach, state, issues))
        {
            return;
        }

        if (!state.Context.FileExists(attach.FilePath))
        {
            issues.Add(Blocker(IssueCodes.FileMissing, $"{attach.FilePath} not found.", attach));
        }
        else if (state.Snapshot.FindVirtualDisk(attach.FilePath) is { } disk)
        {
            issues.Add(Blocker(IssueCodes.AlreadyAttached, $"Already attached as disk {disk.Number}.", attach));
        }
    }

    private static bool CheckPath(VirtualDiskFileOperation operation, PlanState state, List<PlanIssue> issues)
    {
        var path = operation.FilePath;
        var looksRooted = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        var isUnc = path.StartsWith(@"\\", StringComparison.Ordinal);
        var rightExtension = path.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);
        var cleanCharacters = !path.Any(c => c == '"' || char.IsControl(c));

        if ((looksRooted || isUnc) && rightExtension && cleanCharacters)
        {
            if (state.Context.ScriptCanHold(path))
            {
                return true;
            }

            issues.Add(Blocker(IssueCodes.UnsupportedCharacters,
                "diskpart can't handle some characters in that path on this PC.", operation));
            return false;
        }

        issues.Add(Blocker(IssueCodes.InvalidPath,
            "Needs a full path ending in .vhd or .vhdx.", operation));
        return false;
    }

    private static string Style(PartitionStyle style) => style == PartitionStyle.Raw ? "uninitialised" : style.ToString().ToUpperInvariant();

    private static PlanIssue Blocker(string code, string message, DiskOperation operation) =>
        new(IssueSeverity.Blocker, code, message) { Operation = operation };
}
