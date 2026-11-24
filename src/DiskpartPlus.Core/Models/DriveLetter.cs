namespace DiskpartPlus.Core.Models;

/// <summary>A drive letter from A to Z, always stored in upper case.</summary>
public readonly record struct DriveLetter
{
    public DriveLetter(char value)
    {
        var upper = char.ToUpperInvariant(value);
        if (upper is < 'A' or > 'Z')
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A drive letter has to be between A and Z.");
        }

        Value = upper;
    }

    public char Value { get; }

    /// <summary>The letter with a colon after it, like "E:".</summary>
    public string WithColon => Value + ":";

    // Accepts "e", "E:", "E:\" and so on.
    public static bool TryParse(string? text, out DriveLetter letter)
    {
        letter = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim().TrimEnd('\\').TrimEnd(':');
        if (trimmed.Length != 1)
        {
            return false;
        }

        var c = char.ToUpperInvariant(trimmed[0]);
        if (c is < 'A' or > 'Z')
        {
            return false;
        }

        letter = new DriveLetter(c);
        return true;
    }

    public static DriveLetter Parse(string text) =>
        TryParse(text, out var letter) ? letter : throw new FormatException($"\"{text}\" isn't a drive letter.");

    public override string ToString() => Value.ToString();
}
