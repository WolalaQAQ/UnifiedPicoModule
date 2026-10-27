using System.Net;
using System.Net.Sockets;
using UnifiedPicoModule.Runtime;

namespace UnifiedPicoModule.Transport;

public sealed class TjrUdpReceiver : IAsyncDisposable
{
    private readonly UdpClient socket;
    private readonly TongueReceiver receiver;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task pump;
    public int Port { get; }

    public TjrUdpReceiver(TongueReceiver receiver, int port = 45682,
        Action<ReceiveResult>? onPacket = null, Action<string>? log = null)
    {
        this.receiver = receiver;
        socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        pump = Run(onPacket, log);
    }

    private async Task Run(Action<ReceiveResult>? onPacket, Action<string>? log)
    {
        try
        {
            while (true)
            {
                var incoming = await socket.ReceiveAsync(cancellation.Token).ConfigureAwait(false);
                if (!IPAddress.IsLoopback(incoming.RemoteEndPoint.Address)) continue;
                var result = receiver.Receive(incoming.Buffer);
                onPacket?.Invoke(result); // Never write SDK state from this worker.
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (SocketException error) { log?.Invoke($"Tongue TJR1 receive stopped: {error.Message}"); }
        finally { receiver.Stop(); }
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        try { await pump.ConfigureAwait(false); }
        finally { socket.Dispose(); cancellation.Dispose(); receiver.Stop(); }
    }
}
