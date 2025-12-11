#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Client-side communicator for sending messages to servers.
/// Uses a dedicated thread for sending to avoid blocking.
/// </summary>
public sealed class XClientCommunicator : IClientCommunicator, IDisposable
{
    private readonly IPlaySocket _socket;
    private readonly BlockingCollection<SendItem> _sendQueue;
    private readonly ConcurrentDictionary<string, string> _connections = new();
    private readonly Thread _sendThread;
    private readonly CancellationTokenSource _cts;
    private bool _disposed;

    /// <inheritdoc/>
    public string Nid => _socket.Nid;

    /// <summary>
    /// Initializes a new instance of the <see cref="XClientCommunicator"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket.</param>
    /// <param name="queueSize">Maximum queue size for pending sends.</param>
    public XClientCommunicator(IPlaySocket socket, int queueSize = 10000)
    {
        _socket = socket;
        _sendQueue = new BlockingCollection<SendItem>(queueSize);
        _cts = new CancellationTokenSource();

        _sendThread = new Thread(SendLoop)
        {
            Name = $"PlayHouse-Send-{Nid}",
            IsBackground = true
        };
        _sendThread.Start();
    }

    /// <inheritdoc/>
    public void Send(string targetNid, RuntimeRoutePacket packet)
    {
        if (_disposed) return;

        var item = new SendItem(targetNid, packet.SerializeHeader(), packet.GetPayloadBytes());
        if (!_sendQueue.TryAdd(item, TimeSpan.FromMilliseconds(100)))
        {
            // Queue full - log warning or handle backpressure
            item.Dispose();
        }
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

    private void SendLoop()
    {
        try
        {
            foreach (var item in _sendQueue.GetConsumingEnumerable(_cts.Token))
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _sendQueue.CompleteAdding();

        // Wait for send thread to finish
        if (_sendThread.IsAlive)
        {
            _sendThread.Join(TimeSpan.FromSeconds(3));
        }

        // Drain remaining items
        while (_sendQueue.TryTake(out var item))
        {
            item.Dispose();
        }

        _sendQueue.Dispose();
        _cts.Dispose();
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
            // Could implement pooling here for high-performance scenarios
        }
    }
}
