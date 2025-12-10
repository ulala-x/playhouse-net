#nullable enable

using PlayHouse.Runtime.Message;
using PlayHouse.Runtime.PlaySocket;

namespace PlayHouse.Runtime.Communicator;

/// <summary>
/// Server-side communicator for receiving messages from clients/servers.
/// Uses a dedicated thread for receiving.
/// </summary>
public sealed class XServerCommunicator : IServerCommunicator, IDisposable
{
    private readonly IPlaySocket _socket;
    private readonly Thread _receiveThread;
    private readonly CancellationTokenSource _cts;
    private Action<string, RuntimeRoutePacket>? _handler;
    private volatile bool _running;
    private bool _disposed;

    /// <inheritdoc/>
    public string Nid => _socket.Nid;

    /// <summary>
    /// Initializes a new instance of the <see cref="XServerCommunicator"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket.</param>
    public XServerCommunicator(IPlaySocket socket)
    {
        _socket = socket;
        _cts = new CancellationTokenSource();

        _receiveThread = new Thread(ReceiveLoop)
        {
            Name = $"PlayHouse-Recv-{Nid}",
            IsBackground = true
        };
    }

    /// <summary>
    /// Binds the socket to an address.
    /// </summary>
    /// <param name="address">Bind address.</param>
    public void Bind(string address)
    {
        _socket.Bind(address);
    }

    /// <inheritdoc/>
    public void OnReceive(Action<string, RuntimeRoutePacket> handler)
    {
        _handler = handler;
    }

    /// <inheritdoc/>
    public void Start()
    {
        if (_running) return;

        _running = true;
        _receiveThread.Start();
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (!_running) return;

        _running = false;
        _cts.Cancel();

        if (_receiveThread.IsAlive)
        {
            _receiveThread.Join(TimeSpan.FromSeconds(3));
        }
    }

    private void ReceiveLoop()
    {
        const int TimeoutMs = 100;

        while (_running && !_cts.IsCancellationRequested)
        {
            try
            {
                if (_socket.Receive(TimeoutMs, out var senderNid, out var headerBytes, out var payload))
                {
                    var packet = RuntimeRoutePacket.FromFrames(headerBytes, payload);
                    _handler?.Invoke(senderNid, packet);
                }
            }
            catch (Exception ex)
            {
                // Log error but continue receiving
                Console.Error.WriteLine($"[XServerCommunicator] Receive error: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _cts.Dispose();
        _socket.Dispose();
    }
}
