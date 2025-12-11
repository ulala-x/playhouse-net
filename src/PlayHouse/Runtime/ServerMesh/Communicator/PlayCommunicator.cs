#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Combined communicator for bidirectional server-to-server communication.
/// Manages both send and receive operations with dedicated threads.
/// </summary>
public sealed class PlayCommunicator : ICommunicator
{
    private readonly IPlaySocket _socket;
    private readonly BlockingCollection<SendItem> _sendQueue;
    private readonly ConcurrentDictionary<string, string> _connections = new();

    private Thread? _sendThread;
    private Thread? _receiveThread;
    private CancellationTokenSource? _cts;
    private Action<string, RuntimeRoutePacket>? _handler;
    private bool _disposed;

    /// <inheritdoc/>
    public string Nid => _socket.Nid;

    /// <inheritdoc/>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayCommunicator"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket.</param>
    /// <param name="sendQueueSize">Maximum queue size for pending sends.</param>
    public PlayCommunicator(IPlaySocket socket, int sendQueueSize = 10000)
    {
        _socket = socket;
        _sendQueue = new BlockingCollection<SendItem>(sendQueueSize);
    }

    /// <summary>
    /// Creates a PlayCommunicator from ServerConfig.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <returns>A new PlayCommunicator instance.</returns>
    public static PlayCommunicator Create(ServerConfig config)
    {
        var socket = NetMqPlaySocket.Create(config);
        return new PlayCommunicator(socket);
    }

    /// <inheritdoc/>
    public void Bind(string address)
    {
        _socket.Bind(address);
    }

    /// <inheritdoc/>
    public void Connect(string targetNid, string address)
    {
        if (_disposed) return;

        if (_connections.TryAdd(targetNid, address))
        {
            _socket.Connect(address);
        }
    }

    /// <inheritdoc/>
    public void Disconnect(string targetNid)
    {
        if (_disposed) return;

        if (_connections.TryRemove(targetNid, out var address))
        {
            _socket.Disconnect(address);
        }
    }

    /// <inheritdoc/>
    public void Send(string targetNid, RuntimeRoutePacket packet)
    {
        if (_disposed || !IsRunning) return;

        var item = new SendItem(targetNid, packet.SerializeHeader(), packet.GetPayloadBytes());
        if (!_sendQueue.TryAdd(item, TimeSpan.FromMilliseconds(100)))
        {
            item.Dispose();
            // Could log warning about queue overflow
        }
    }

    /// <inheritdoc/>
    public void OnReceive(Action<string, RuntimeRoutePacket> handler)
    {
        _handler = handler;
    }

    /// <inheritdoc/>
    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        IsRunning = true;

        _sendThread = new Thread(SendLoop)
        {
            Name = $"PlayHouse-Send-{Nid}",
            IsBackground = true
        };
        _sendThread.Start();

        _receiveThread = new Thread(ReceiveLoop)
        {
            Name = $"PlayHouse-Recv-{Nid}",
            IsBackground = true
        };
        _receiveThread.Start();
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts?.Cancel();
        _sendQueue.CompleteAdding();

        // Wait for threads to finish
        _sendThread?.Join(TimeSpan.FromSeconds(3));
        _receiveThread?.Join(TimeSpan.FromSeconds(3));
    }

    private void SendLoop()
    {
        try
        {
            foreach (var item in _sendQueue.GetConsumingEnumerable(_cts!.Token))
            {
                try
                {
                    _socket.Send(item.TargetNid, item.HeaderBytes, item.PayloadBytes);
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void ReceiveLoop()
    {
        const int TimeoutMs = 100;

        while (IsRunning && !_cts!.IsCancellationRequested)
        {
            try
            {
                if (_socket.Receive(TimeoutMs, out var senderNid, out var headerBytes, out var payload))
                {
                    var packet = RuntimeRoutePacket.FromFrames(headerBytes, payload);
                    _handler?.Invoke(senderNid, packet);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[PlayCommunicator] Receive error: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        // Drain remaining items
        while (_sendQueue.TryTake(out var item))
        {
            item.Dispose();
        }

        _sendQueue.Dispose();
        _cts?.Dispose();
        _socket.Dispose();
    }

    private sealed class SendItem : IDisposable
    {
        public string TargetNid { get; }
        public byte[] HeaderBytes { get; }
        public byte[] PayloadBytes { get; }

        public SendItem(string targetNid, byte[] headerBytes, byte[] payloadBytes)
        {
            TargetNid = targetNid;
            HeaderBytes = headerBytes;
            PayloadBytes = payloadBytes;
        }

        public void Dispose()
        {
            // Could implement pooling here
        }
    }
}
