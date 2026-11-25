using Microsoft.Win32;

namespace Heracles.Tests;

/// <summary>
/// The version Windows shows for an installed copy, kept right by the app
/// after it has updated itself. Every test works on a registry key of its
/// own, never on the real entry.
/// </summary>
public sealed class InstalledCopyTests : IDisposable
{
    private static readonly string Separator = ((char)92).ToString();
    private static readonly string Folder = string.Join(Separator, "C:", "Users", "jdoe", "AppData", "Local", "Programs", "Heracles");
    private readonly string keyPath = string.Join(Separator, "Software", "Heracles.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);

    /// <summary>An entry as the installer leaves it.</summary>
    private void Install(string version, string? location = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue("InstallLocation", location ?? Folder + Separator);
        key.SetValue("DisplayName", "Heracles");
        key.SetValue("DisplayVersion", version);
        key.SetValue("MajorVersion", 0, RegistryValueKind.DWord);
        key.SetValue("MinorVersion", 3, RegistryValueKind.DWord);
        key.SetValue("VersionMajor", 0, RegistryValueKind.DWord);
        key.SetValue("VersionMinor", 3, RegistryValueKind.DWord);
    }

    private object? Value(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name);
    }

    [Fact]
    public void ACopyThatUpdatedItselfSaysSo()
    {
        Install("0.3.1");
        Assert.True(InstalledCopy.RecordVersion(Registry.CurrentUser, keyPath, Folder, "1.4.2"));
        Assert.Equal("1.4.2", Value("DisplayVersion"));
        Assert.Equal(1, Value("MajorVersion"));
        Assert.Equal(1, Value("VersionMajor"));
        Assert.Equal(4, Value("MinorVersion"));
        Assert.Equal(4, Value("VersionMinor"));
        Assert.Equal("Heracles", Value("DisplayName"));
    }

    [Fact]
    public void TheFolderIsTheSameHoweverItIsWritten()
    {
        Install("0.3.1", Folder.ToUpperInvariant());
        Assert.True(InstalledCopy.RecordVersion(Registry.CurrentUser, keyPath, Folder + Separator, "0.3.2"));
        Assert.Equal("0.3.2", Value("DisplayVersion"));
    }

    [Fact]
    public void NothingIsWrittenWhenTheVersionIsAlreadyRight()
    {
        Install("0.3.1");
        Assert.False(InstalledCopy.RecordVersion(Registry.CurrentUser, keyPath, Folder, "0.3.1"));
    }

    [Fact]
    public void ACopyElsewhereLeavesTheInstalledOnesEntryAlone()
    {
        Install("0.3.1");
        var elsewhere = string.Join(Separator, "C:", "Users", "jdoe", "Downloads", "Heracles");
        Assert.False(InstalledCopy.RecordVersion(Registry.CurrentUser, keyPath, elsewhere, "0.3.2"));
        Assert.Equal("0.3.1", Value("DisplayVersion"));
    }

    [Fact]
    public void ACopyThatWasNeverInstalledMakesNoEntry()
    {
        Assert.False(InstalledCopy.RecordVersion(Registry.CurrentUser, keyPath, Folder, "0.3.2"));
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        Assert.Null(key);
    }

    [Fact]
    public void AnEntryWithoutAFolderIsNotThisCopys()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(keyPath)) key.SetValue("DisplayVersion", "0.3.1");
        Assert.False(InstalledCopy.RecordVersion(Registry.CurrentUser, keyPath, Folder, "0.3.2"));
        Assert.Equal("0.3.1", Value("DisplayVersion"));
    }

    [Fact]
    public void TheKeyIsWhereTheInstallerWritesIt()
    {
        // Spelled out once, whole: a backslash lost on the way into the
        // source would leave every installed copy showing the version it
        // was installed as, and nothing would say so.
        var expected = "Software" + Separator + "Microsoft" + Separator + "Windows" + Separator + "CurrentVersion"
            + Separator + "Uninstall" + Separator + "{7E7666E6-6C97-4920-8107-E156E0BFCBB0}_is1";
        Assert.Equal(expected, InstalledCopy.UninstallKey);
        Assert.Equal(5, InstalledCopy.UninstallKey.Count(c => c == (char)92));
    }

    [Fact]
    public void TheAppAndTheInstallerAgreeOnTheAppId()
    {
        // The installer names the registry key after its AppId; the app
        // looks for the key by the same id.
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "installer", "Heracles.iss"));
        Assert.Contains($"#define AppId \"{InstalledCopy.AppId}\"", script);
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "VERSION")) && Directory.Exists(Path.Combine(folder.FullName, "tools"))) return folder.FullName;
        }
        throw new InvalidOperationException("the tests do not run from inside the repository");
    }
}
