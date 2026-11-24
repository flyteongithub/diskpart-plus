namespace DiskpartPlus.Core.Scripting;

/// <summary>
/// A finished diskpart script. This exact text is what the user sees in the preview
/// and what gets handed to "diskpart /s".
/// </summary>
public sealed class DiskpartScript
{
    public DiskpartScript(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var list = lines.ToList();

        // diskpart wants one command per line and no blank lines.
        foreach (var line in list)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Contains('\r') || line.Contains('\n'))
            {
                throw new ArgumentException("Every script line has to be a single, non-empty line.", nameof(lines));
            }
        }

        Lines = list;
    }

    public IReadOnlyList<string> Lines { get; }

    /// <summary>The lines that actually do something, without the "rem" comments.</summary>
    public IEnumerable<string> Commands => Lines.Where(l => !l.StartsWith("rem ", StringComparison.OrdinalIgnoreCase));

    public string ToText() => string.Join("\r\n", Lines) + "\r\n";

    public override string ToString() => ToText();
}
