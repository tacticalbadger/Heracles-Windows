using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Heracles;

/// <summary>
/// One running copy of the app per library. The core's change feed lives
/// inside the process, so two processes on one library would not see each
/// other's changes. The first copy holds a name; a later copy hands its
/// arguments to the holder and exits.
///
/// The name is made from the library's folder, so a test run on a fixture
/// never talks to a copy of the app running on the real library.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>More than any command line the app is given.</summary>
    private const int LongestMessage = 64 * 1024;

    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop = new();

    private SingleInstance(Mutex mutex, string pipe, Action<string[]> onSecondLaunch)
    {
        this.mutex = mutex;
        // The pipe is there before the name is said to be held, so a later
        // copy finds somebody to hand over to however busy this one is.
        // (Made on a borrowed thread a moment later, it could come after a
        // later copy had given up waiting.)
        NamedPipeServerStream? first = null;
        try { first = Open(pipe); }
        catch (IOException) { /* Listen tries again. */ }
        catch (UnauthorizedAccessException) { }
        _ = Task.Run(() => Listen(pipe, first, onSecondLaunch, stop.Token));
    }

    // Only this user's processes can connect.
    private static NamedPipeServerStream Open(string pipe) => new(
        pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>
    /// Take the name. Null when another process, or this one, already holds
    /// it. <paramref name="onSecondLaunch"/> is called with the arguments of
    /// each later copy, on a thread of its own.
    /// </summary>
    public static SingleInstance? Claim(string name, Action<string[]> onSecondLaunch)
    {
        var key = Key(name);
        // The mutex is never locked: that it exists says the name is taken,
        // and it exists for as long as its holder keeps it open.
        var mutex = new Mutex(initiallyOwned: false, @"Local\Heracles-" + key, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, PipeName(name), onSecondLaunch);
    }

    /// <summary>Hand arguments to the holder of the name. False if nobody answered in time.</summary>
    public static bool Send(string name, string[] args, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(name), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)Math.Max(1, timeout.TotalMilliseconds));
            var bytes = Encoding.UTF8.GetBytes(Encode(args) + "\n");
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>The arguments as one line of text.</summary>
    public static string Encode(string[] args) => JsonSerializer.Serialize(args);

    public static string[] Decode(string line)
    {
        try { return JsonSerializer.Deserialize<string[]>(line)?.Where(arg => arg is not null).ToArray() ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>The pipe the holder of a name listens on.</summary>
    internal static string PipeName(string name) => "heracles-" + Key(name);

    /// <summary>The same for a path however its letters are cased, since Windows paths ignore case.</summary>
    private static string Key(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().TrimEnd('\\', '/').ToUpperInvariant()));
        return Convert.ToHexString(hash, 0, 8);
    }

    /// <param name="opened">The pipe made when the name was taken, for the first caller.</param>
    private static async Task Listen(string pipe, NamedPipeServerStream? opened, Action<string[]> onSecondLaunch, CancellationToken token)
    {
        // Given back if the holder goes before anyone has called.
        await using var unused = new Unused(() => opened);
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = opened ?? Open(pipe);
                opened = null;
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var buffer = new byte[LongestMessage];
                var length = 0;
                while (length < buffer.Length)
                {
                    var read = await server.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
                    if (read == 0) break;
                    length += read;
                    if (Array.IndexOf(buffer, (byte)'\n', 0, length) >= 0) break;
                }
                var line = Encoding.UTF8.GetString(buffer, 0, length).Split('\n')[0];
                onSecondLaunch(Decode(line));
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { /* A caller that went away mid-message. Listen for the next. */ }
            catch (ObjectDisposedException) { return; }
        }
    }

    /// <summary>Closes the pipe that was made ahead, if it is still waiting to be used when listening ends.</summary>
    private sealed class Unused(Func<NamedPipeServerStream?> pipe) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => pipe()?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        stop.Cancel();
        mutex.Dispose();
        stop.Dispose();
    }
}
