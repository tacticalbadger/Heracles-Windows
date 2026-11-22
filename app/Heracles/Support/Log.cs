using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Heracles.Support;

/// <summary>
/// The app's log: one line per entry, appended to a file. Tests read it to
/// see what the app did. Writing to it never fails the app.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void Info(string message) => Write("info", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("error", exception is null ? message : $"{message}: {exception}");

    /// <summary>
    /// An entry as it is written. Whatever the message holds, the entry is one
    /// line, so text that came from outside the app cannot forge a second entry.
    /// </summary>
    internal static string Line(DateTime utc, string level, string message)
    {
        var time = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return $"{time} {level} {Regex.Replace(message, "[\r\n]+", " ")}";
    }

    private static void Write(string level, string message)
    {
        var line = Line(DateTime.UtcNow, level, message);
        try
        {
            lock (Gate)
            {
                var folder = Path.GetDirectoryName(AppPaths.LogPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(AppPaths.LogPath, line + "\n", Utf8);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (NotSupportedException) { }
    }
}
