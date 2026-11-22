using System.Text.Json;

namespace Heracles.Support;

/// <summary>
/// One window as it is remembered between runs: where it was, how big, and its
/// panes (a <c>PaneTree</c> as JSON). Named so as not to be taken for WPF's
/// own <c>WindowState</c>.
/// </summary>
internal sealed record SavedWindow(double Left, double Top, double Width, double Height, bool Maximized, string? Panes);

/// <summary>
/// The windows that were open, kept in a file beside the library. The file
/// belongs to an earlier run and may hold anything, so whatever cannot be read
/// is treated as no windows at all, never as an error.
/// </summary>
internal static class WindowStates
{
    public const double SmallestWidth = 400;
    public const double SmallestHeight = 300;

    public static string Serialize(IEnumerable<SavedWindow> windows) => JsonSerializer.Serialize(windows.ToList());

    public static IReadOnlyList<SavedWindow> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        List<SavedWindow?>? read;
        try { read = JsonSerializer.Deserialize<List<SavedWindow?>>(json); }
        catch (JsonException) { return []; }
        catch (NotSupportedException) { return []; }
        if (read is null) return [];

        var windows = new List<SavedWindow>();
        foreach (var window in read)
        {
            if (window is null) continue;
            if (!double.IsFinite(window.Left) || !double.IsFinite(window.Top)) continue;
            if (!double.IsFinite(window.Width) || !double.IsFinite(window.Height)) continue;
            windows.Add(window with
            {
                Width = Math.Max(window.Width, SmallestWidth),
                Height = Math.Max(window.Height, SmallestHeight),
            });
        }
        return windows;
    }

    public static IReadOnlyList<SavedWindow> Load(string path)
    {
        try { return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    /// <summary>Written beside the old file and then swapped in, so a crash cannot leave half a file.</summary>
    public static void Save(string path, IEnumerable<SavedWindow> windows)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, Serialize(windows));
        File.Move(temporary, path, overwrite: true);
    }
}
