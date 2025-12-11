#nullable enable

using System.Text;
using NetMQ;
using NetMQ.Sockets;

namespace PlayHouse.Runtime.ServerMesh.PlaySocket;

/// <summary>
/// NetMQ Router socket implementation for server-to-server communication.
/// </summary>
/// <remarks>
/// Uses Router-Router pattern for bidirectional messaging.
/// The socket identity is set to the NID for routing purposes.
/// </remarks>
public sealed class NetMqPlaySocket : IPlaySocket
{
    private readonly RouterSocket _socket;
    private readonly object _sendLock = new();
    private bool _disposed;

    /// <inheritdoc/>
    public string Nid { get; }

    /// <inheritdoc/>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMqPlaySocket"/> class.
    /// </summary>
    /// <param name="nid">Node ID for this socket.</param>
    /// <param name="config">Socket configuration.</param>
    public NetMqPlaySocket(string nid, PlaySocketConfig? config = null)
    {
        Nid = nid;
        config ??= PlaySocketConfig.Default;

        _socket = new RouterSocket();

        // Set socket identity to NID for routing
        _socket.Options.Identity = Encoding.UTF8.GetBytes(nid);

        // Router options
        _socket.Options.RouterHandover = true;
        _socket.Options.RouterMandatory = true;

        // High water marks
        _socket.Options.SendHighWatermark = config.SendHighWatermark;
        _socket.Options.ReceiveHighWatermark = config.ReceiveHighWatermark;

        // TCP options
        _socket.Options.TcpKeepalive = config.TcpKeepalive;
        if (config.TcpKeepalive)
        {
            _socket.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(config.TcpKeepaliveIdle);
            _socket.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(config.TcpKeepaliveInterval);
        }

        _socket.Options.Linger = TimeSpan.FromMilliseconds(config.Linger);
    }

    /// <summary>
    /// Creates a NetMQPlaySocket from ServerConfig.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <returns>A new NetMQPlaySocket instance.</returns>
    public static NetMqPlaySocket Create(ServerConfig config)
    {
        return new NetMqPlaySocket(config.Nid, PlaySocketConfig.FromServerConfig(config));
    }

    /// <inheritdoc/>
    public void Bind(string address)
    {
        ThrowIfDisposed();
        _socket.Bind(address);
        IsActive = true;
    }

    /// <inheritdoc/>
    public void Connect(string address)
    {
        ThrowIfDisposed();
        _socket.Connect(address);
        IsActive = true;
    }

    /// <inheritdoc/>
    public void Disconnect(string address)
    {
        ThrowIfDisposed();
        _socket.Disconnect(address);
    }

    /// <inheritdoc/>
    public bool Send(string targetNid, ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> payload)
    {
        ThrowIfDisposed();

        lock (_sendLock)
        {
            try
            {
                // Frame 0: Target NID (routing identity)
                _socket.SendMoreFrame(Encoding.UTF8.GetBytes(targetNid));
                // Frame 1: RouteHeader
                _socket.SendMoreFrame(headerBytes.ToArray());
                // Frame 2: Payload
                _socket.SendFrame(payload.ToArray());
                return true;
            }
            catch (HostUnreachableException)
            {
                // Target not connected yet - RouterMandatory throws this
                return false;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryReceive(out string senderNid, out byte[] headerBytes, out byte[] payload)
    {
        ThrowIfDisposed();

        senderNid = string.Empty;
        headerBytes = Array.Empty<byte>();
        payload = Array.Empty<byte>();

        if (!_socket.TryReceiveFrameString(out senderNid))
        {
            return false;
        }

        if (!_socket.TryReceiveFrameBytes(out headerBytes))
        {
            return false;
        }

        if (!_socket.TryReceiveFrameBytes(out payload))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool Receive(int timeoutMs, out string senderNid, out byte[] headerBytes, out byte[] payload)
    {
        ThrowIfDisposed();

        senderNid = string.Empty;
        headerBytes = Array.Empty<byte>();
        payload = Array.Empty<byte>();

        var timeout = timeoutMs < 0 ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(timeoutMs);

        if (!_socket.TryReceiveFrameString(timeout, out senderNid!))
        {
            return false;
        }

        // Once we get the first frame, get the rest without timeout
        headerBytes = _socket.ReceiveFrameBytes();
        payload = _socket.ReceiveFrameBytes();

        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NetMqPlaySocket));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsActive = false;
        _socket.Dispose();
    }
}
