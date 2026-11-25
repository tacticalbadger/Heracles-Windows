namespace Heracles.Tests;

/// <summary>
/// A web server of the test's own, on this machine only. It answers each
/// request with what the test says, and nothing leaves the machine.
/// </summary>
internal sealed class LocalWeb : IDisposable
{
    private readonly System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
    private readonly Func<string, (string Status, string Type, byte[] Body, long? SaidLength)> answer;

    public int Port { get; }

    public List<string> Asked { get; } = [];

    public LocalWeb(Func<string, (string Status, string Type, byte[] Body, long? SaidLength)> answer)
    {
        this.answer = answer;
        listener.Start();
        Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        _ = Serve();
    }

    public string Address(string name) => $"http://127.0.0.1:{Port}/{name}";

    public void Dispose() => listener.Stop();

    private async Task Serve()
    {
        while (true)
        {
            System.Net.Sockets.TcpClient client;
            try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch (Exception error) when (error is System.Net.Sockets.SocketException or ObjectDisposedException or InvalidOperationException) { return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private void Handle(System.Net.Sockets.TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                // The request, as far as the empty line that ends its headers.
                var request = new System.Text.StringBuilder();
                var buffer = new byte[1];
                while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && stream.Read(buffer, 0, 1) == 1)
                {
                    request.Append((char)buffer[0]);
                }
                var path = request.ToString().Split(' ').Skip(1).FirstOrDefault() ?? "/";
                lock (Asked) Asked.Add(path);
                var (status, type, body, saidLength) = answer(path);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {saidLength ?? body.Length}\r\nConnection: close\r\n\r\n";
                stream.Write(System.Text.Encoding.ASCII.GetBytes(head));
                stream.Write(body);
                stream.Flush();
            }
        }
        catch (IOException) { /* The other end hung up: it had heard enough. */ }
    }
}
