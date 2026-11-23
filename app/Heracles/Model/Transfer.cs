namespace Heracles.Model;

/// <summary>
/// What the app decides about files going in and out. The reading and the
/// writing are the core's; the app says which files a dialog offers, and
/// what an exported note is called.
/// </summary>
internal static class Transfer
{
    /// <summary>The kinds of file that can be imported: the Mac's list.</summary>
    public static IReadOnlyList<string> ImportExtensions { get; } = ["md", "markdown", "mdown", "mkd", "mkdn", "txt", "html", "htm"];

    /// <summary>What an open-file dialog offers: those kinds, or everything.</summary>
    public static string ImportFilter { get; } =
        "Markdown, text and HTML|" + string.Join(";", ImportExtensions.Select(extension => "*." + extension)) + "|All files|*.*";

    public const string ExportFilter = "Markdown|*.md";

    private const string ExportExtension = ".md";

    /// <summary>The longest name offered, before its extension. A path has little room to spare on Windows.</summary>
    private const int LongestName = 120;

    /// <summary>Names Windows keeps for devices, whatever comes after a dot.</summary>
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Whether Windows refuses a character in a file name. (92 is the backslash.)</summary>
    private static bool IsRefused(char character) =>
        character < ' ' || character is '/' or (char)92 or ':' or '*' or '?' or '"' or '<' or '>' or '|';

    /// <summary>Windows drops dots and spaces from the end of a name, so none are left there.</summary>
    private static string Ended(string name) => name.TrimEnd('.', ' ');

    /// <summary>
    /// The file name offered for a note: its title, made into a name Windows
    /// accepts, with <c>.md</c>.
    /// </summary>
    public static string ExportFileName(string title)
    {
        var name = new string(title.Select(character => IsRefused(character) ? '-' : character).ToArray());
        name = Ended(name.Trim());

        if (name.Length > LongestName)
        {
            // Not through the middle of a character that takes two units.
            var cut = char.IsHighSurrogate(name[LongestName - 1]) ? LongestName - 1 : LongestName;
            name = Ended(name[..cut]);
        }
        if (name.Length == 0) name = "Untitled";

        var dot = name.IndexOf('.');
        if (Devices.Contains(dot < 0 ? name : name[..dot])) name = "_" + name;
        return name + ExportExtension;
    }
}
