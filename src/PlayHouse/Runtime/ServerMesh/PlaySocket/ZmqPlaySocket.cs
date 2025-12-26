#nullable enable

using System.Collections.Concurrent;
using System.Text;
using Net.Zmq;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Runtime.ServerMesh.PlaySocket;

/// <summary>
/// Net.Zmq Router socket wrapper for server-to-server communication.
/// </summary>
/// <remarks>
/// Wraps a single Router socket. PlayCommunicator creates separate instances
/// for server (Bind + Receive) and client (Connect + Send) operations.
/// </remarks>
internal sealed class ZmqPlaySocket : IPlaySocket
{
    private readonly Context _context;
    private readonly Socket _socket;
    private readonly byte[] _serverIdBytes; // Cached UTF-8 encoded ServerId
    private readonly byte[] _recvServerIdBuffer = new byte[1024];      // 1KB - for Recv
    private readonly byte[] _recvHeaderBuffer = new byte[65536];       // 64KB - for Recv
    private readonly ConcurrentDictionary<string, byte[]> _serverIdCache = new(); // Cache for target ServerId
    private bool _disposed;
    private string? _boundEndpoint;

    /// <inheritdoc/>
    public string ServerId { get; }

    /// <inheritdoc/>
    public string EndPoint => _boundEndpoint ?? string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZmqPlaySocket"/> class.
    /// </summary>
    /// <param name="serverId">Server ID for this socket.</param>
    /// <param name="context">ZMQ context (required, should be shared across sockets).</param>
    /// <param name="config">Socket configuration.</param>
    public ZmqPlaySocket(string serverId, Context context, PlaySocketConfig? config = null)
    {
        ServerId = serverId;
        _serverIdBytes = Encoding.UTF8.GetBytes(serverId);
        config ??= PlaySocketConfig.Default;

        // Context management - always provided externally
        _context = context;

        // Single Router socket
        _socket = new Socket(_context, SocketType.Router);
        ConfigureSocket(_socket, config);
    }

    /// <summary>
    /// Configures socket options for Router socket.
    /// </summary>
    private void ConfigureSocket(Socket socket, PlaySocketConfig config)
    {
        socket.SetOption(SocketOption.Routing_Id, _serverIdBytes);
        socket.SetOption(SocketOption.Router_Handover, 1);
        socket.SetOption(SocketOption.Router_Mandatory, 1);
        socket.SetOption(SocketOption.Immediate, 0); // DelayAttachOnConnect equivalent
        socket.SetOption(SocketOption.Sndhwm, config.SendHighWatermark);
        socket.SetOption(SocketOption.Rcvhwm, config.ReceiveHighWatermark);
        socket.SetOption(SocketOption.Rcvtimeo, config.ReceiveTimeout); // 무한 대기, Context.Shutdown()로 해제
        socket.SetOption(SocketOption.Linger, config.Linger);

        if (config.TcpKeepalive)
        {
            socket.SetOption(SocketOption.Tcp_Keepalive, 1);
            socket.SetOption(SocketOption.Tcp_Keepalive_Idle, config.TcpKeepaliveIdle);
            socket.SetOption(SocketOption.Tcp_Keepalive_Intvl, config.TcpKeepaliveInterval);
        }
    }

    /// <inheritdoc/>
    public void Bind(string endpoint)
    {
        ThrowIfDisposed();
        _socket.Bind(endpoint);
        _boundEndpoint = endpoint;
    }

    /// <inheritdoc/>
    public void Connect(string endpoint)
    {
        ThrowIfDisposed();
        _socket.Connect(endpoint);
    }

    /// <inheritdoc/>
    public void Disconnect(string endpoint)
    {
        ThrowIfDisposed();
        _socket.Disconnect(endpoint);
    }

    /// <inheritdoc/>
    public void Send(string serverId, RoutePacket packet)
    {
        ThrowIfDisposed();

        // Note: No lock needed - Action queue in XClientCommunicator provides serialization
        using (packet)
        {
            try
            {
                // Set PayloadSize in header for MessagePool.Rent on receiver side
                packet.Header.PayloadSize = (uint)packet.Payload.Length;

                // Frame 0: Target ServerId (SendMore) - Cached
                var serverIdBytes = _serverIdCache.GetOrAdd(serverId, static id => Encoding.UTF8.GetBytes(id));
                _socket.Send(serverIdBytes, SendFlags.SendMore);

                // Frame 1: RouteHeader (SendMore)
                var headerBytes = packet.SerializeHeader();
                _socket.Send(headerBytes, SendFlags.SendMore);

                // Frame 2: Payload (마지막 프레임) - Simple and fast
                Net.Zmq.Message message = MessagePool.Shared.Rent(packet.Payload.DataSpan);
                _socket.Send(message);
            }
            catch (Exception ex)
            {
                // RouterMandatory causes send to fail if target not connected
                Console.Error.WriteLine($"[ZmqPlaySocket] Failed to send to {serverId}: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public RoutePacket? Receive()
    {
        ThrowIfDisposed();

        // Frame 0: Sender ServerId (고정 버퍼)
        int serverIdLen = _socket.Recv(_recvServerIdBuffer);
        if (serverIdLen <= 0)
        {
            // Timeout or error - return null
            return null;
        }

        var senderServerId = Encoding.UTF8.GetString(_recvServerIdBuffer, 0, serverIdLen);

        // Frame 1: RouteHeader (고정 버퍼)
        int headerLen = _socket.Recv(_recvHeaderBuffer);
        if (headerLen <= 0)
        {
            return null;
        }

        // Parse header to get payload_size
        var header = PlayHouse.Runtime.Proto.RouteHeader.Parser.ParseFrom(_recvHeaderBuffer.AsSpan(0, headerLen));

        // Frame 2: Payload - Use MessagePool.Rent with payload_size from header
        var payloadSize = (int)header.PayloadSize;
        var payloadMessage = MessagePool.Shared.Rent(payloadSize);
        _socket.Recv(payloadMessage, payloadSize);

        // RoutePacket 생성 (Message 수명 관리 위임)
        return RoutePacket.FromFrames(
            header,
            payloadMessage,
            senderServerId);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZmqPlaySocket));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();

        // Context는 외부에서 관리하므로 Dispose 하지 않음
        // PlayCommunicator에서 Context.Dispose() 호출
    }
}
