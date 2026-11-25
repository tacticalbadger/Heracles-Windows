using System.Globalization;
using System.Reflection;

namespace Heracles.Support;

/// <summary>
/// What a build says it is: the version people see, and the build number,
/// which is what tells a newer build from an older one. The number is the
/// commit count (tools\build.ps1 passes it), so it only ever goes up.
/// </summary>
internal readonly record struct AppVersion(string Version, int Build)
{
    /// <summary>
    /// Read from a file version, "0.3.0.52". What cannot be read is build 0:
    /// older than anything published, and what a build made by hand says.
    /// </summary>
    public static AppVersion Parse(string? fileVersion)
    {
        var parts = (fileVersion ?? "").Split('.');
        if (parts.Length != 4 || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var build))
        {
            return new AppVersion(fileVersion ?? "", 0);
        }
        return new AppVersion(string.Join('.', parts[..3]), build);
    }

    /// <summary>This build's own.</summary>
    public static AppVersion Current { get; } =
        Parse(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
}
