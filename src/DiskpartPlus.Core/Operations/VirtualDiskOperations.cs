using DiskpartPlus.Core.Models;

namespace DiskpartPlus.Core.Operations;

/// <summary>What to put on a brand new virtual disk once it's attached.</summary>
public sealed record VirtualDiskSetup(PartitionStyle Style, FormatSpec Format, DriveAssignment? Assign = null);

public sealed record CreateVirtualDisk(
    string FilePath,
    ByteSize MaximumSize,
    VirtualDiskAllocation Allocation = VirtualDiskAllocation.Expandable,
    bool Attach = true,
    VirtualDiskSetup? Setup = null) : VirtualDiskFileOperation(FilePath)
{
    public override string Describe()
    {
        var kind = Allocation == VirtualDiskAllocation.Fixed ? "fixed" : "expandable";
        var text = $"Create {MaximumSize} {kind} VHD at {FilePath}";

        if (Attach || Setup is not null)
        {
            text += ", attach";
        }

        if (Setup is not null)
        {
            text += $", {Setup.Style.ToString().ToUpperInvariant()}, {Setup.Format.Describe()}";
        }

        return text;
    }
}

public sealed record AttachVirtualDisk(string FilePath, bool ReadOnly = false) : VirtualDiskFileOperation(FilePath)
{
    public override string Describe() => $"Attach {FilePath}" + (ReadOnly ? " as read-only" : string.Empty);
}

public sealed record DetachVirtualDisk(string FilePath) : VirtualDiskFileOperation(FilePath)
{
    public override string Describe() => $"Detach {FilePath}";
}
