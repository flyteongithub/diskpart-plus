using System.Globalization;

namespace DiskpartPlus.Core.Models;

/// <summary>
/// A size in bytes. Shown in binary units (1 GB = 1024 MB) because that's what Windows shows,
/// so the numbers here match Explorer and Disk Management.
/// </summary>
public readonly record struct ByteSize(long Bytes) : IComparable<ByteSize>
{
    public const long Kilobyte = 1024;
    public const long Megabyte = Kilobyte * 1024;
    public const long Gigabyte = Megabyte * 1024;
    public const long Terabyte = Gigabyte * 1024;

    public static ByteSize Zero => default;

    public static ByteSize FromMegabytes(long megabytes) => new(checked(megabytes * Megabyte));

    public static ByteSize FromGigabytes(long gigabytes) => new(checked(gigabytes * Gigabyte));

    public static ByteSize FromTerabytes(long terabytes) => new(checked(terabytes * Terabyte));

    // diskpart only deals in whole megabytes, so anything smaller is dropped.
    public long WholeMegabytes => Bytes / Megabyte;

    public double TotalGigabytes => (double)Bytes / Gigabyte;

    public int CompareTo(ByteSize other) => Bytes.CompareTo(other.Bytes);

    public static ByteSize Max(ByteSize a, ByteSize b) => a >= b ? a : b;

    public static ByteSize Min(ByteSize a, ByteSize b) => a <= b ? a : b;

    public static ByteSize operator +(ByteSize a, ByteSize b) => new(checked(a.Bytes + b.Bytes));

    public static ByteSize operator -(ByteSize a, ByteSize b) => new(checked(a.Bytes - b.Bytes));

    public static bool operator <(ByteSize a, ByteSize b) => a.Bytes < b.Bytes;

    public static bool operator >(ByteSize a, ByteSize b) => a.Bytes > b.Bytes;

    public static bool operator <=(ByteSize a, ByteSize b) => a.Bytes <= b.Bytes;

    public static bool operator >=(ByteSize a, ByteSize b) => a.Bytes >= b.Bytes;

    public override string ToString() => ToString(CultureInfo.InvariantCulture);

    public string ToString(IFormatProvider provider)
    {
        var magnitude = Math.Abs(Bytes);
        if (magnitude < Kilobyte)
        {
            return Bytes.ToString(provider) + " B";
        }

        var (divisor, unit) = magnitude switch
        {
            >= Terabyte => (Terabyte, "TB"),
            >= Gigabyte => (Gigabyte, "GB"),
            >= Megabyte => (Megabyte, "MB"),
            _ => (Kilobyte, "KB"),
        };

        // One decimal place is plenty. "512 MB" also reads better than "512.0 MB".
        var value = (double)Bytes / divisor;
        return value.ToString("0.#", provider) + " " + unit;
    }
}
