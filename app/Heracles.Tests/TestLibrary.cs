namespace Heracles.Tests;

/// <summary>A real core on a library in a throwaway folder, removed afterwards.</summary>
internal sealed class TestLibrary : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"heracles-tests-{Guid.NewGuid()}");

    public Store Store { get; }

    public TestLibrary()
    {
        Store = new Store(Path.Combine(Folder, "library"));
    }

    public static string Document(string text) =>
        $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""";

    public void Dispose()
    {
        Store.Dispose();
        // Temporary files are best effort: a leftover must not fail a test.
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>What every test run does first.</summary>
internal static class TestRun
{
    /// <summary>
    /// Whatever the code under test writes to the log goes to a file of the
    /// tests' own, not into the log of the app the user runs.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Begin()
    {
        var log = Path.Combine(Path.GetTempPath(), "heracles-tests-log", "heracles.log");
        Environment.SetEnvironmentVariable(AppPaths.LogPathVariable, log);
    }
}
