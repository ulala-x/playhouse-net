#nullable enable

using System.Text;
using Net.Zmq;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Runtime.ServerMesh.PlaySocket;

/// <summary>
/// Net.Zmq Router socket implementation for server-to-server communication.
/// </summary>
/// <remarks>
/// Uses dual Router sockets for bidirectional messaging:
/// - Server socket: Bind and Receive
/// - Client socket: Connect and Send
/// This separation allows self-messaging (sending to own server socket).
/// </remarks>
internal sealed class ZmqPlaySocket : IPlaySocket
{
    private readonly string _bindEndpoint;
    private readonly Context _context;
    private readonly bool _ownsContext; // True if we created the context and must dispose it
    private readonly Socket _serverSocket;  // For Bind + Receive
    private readonly Socket _clientSocket;  // For Connect + Send
    private readonly byte[] _serverIdBytes; // Cached UTF-8 encoded ServerId
    private readonly byte[] _recvServerIdBuffer = new byte[1024];      // 1KB - for Recv
    private readonly byte[] _recvHeaderBuffer = new byte[65536];       // 64KB - for Recv
    private bool _disposed;

    /// <inheritdoc/>
    public string ServerId { get; }

    /// <inheritdoc/>
    public string EndPoint => _bindEndpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZmqPlaySocket"/> class.
    /// </summary>
    /// <param name="serverId">Server ID for this socket.</param>
    /// <param name="bindEndpoint">Bind endpoint address (e.g., "tcp://*:5555").</param>
    /// <param name="config">Socket configuration.</param>
    /// <param name="context">Optional ZMQ context (creates new if null).</param>
    public ZmqPlaySocket(string serverId, string bindEndpoint, PlaySocketConfig? config = null, Context? context = null)
    {
        ServerId = serverId;
        _bindEndpoint = bindEndpoint;
        _serverIdBytes = Encoding.UTF8.GetBytes(serverId);
        config ??= PlaySocketConfig.Default;

        // Context management
        _ownsContext = context == null;
        _context = context ?? new Context();

        // Server socket - for receiving messages
        _serverSocket = new Socket(_context, SocketType.Router);
        ConfigureServerSocket(_serverSocket, config);

        // Client socket - for sending messages
        _clientSocket = new Socket(_context, SocketType.Router);
        ConfigureClientSocket(_clientSocket, config);
    }

    /// <summary>
    /// Configures server socket options (bind + receive).
    /// </summary>
    private void ConfigureServerSocket(Socket socket, PlaySocketConfig config)
    {
        socket.SetOption(SocketOption.Routing_Id, _serverIdBytes);
        socket.SetOption(SocketOption.Router_Handover, 1);
        socket.SetOption(SocketOption.Rcvhwm, config.ReceiveHighWatermark);
        socket.SetOption(SocketOption.Rcvtimeo, 1000); // 1000ms (1 second) timeout for Receive()
        socket.SetOption(SocketOption.Linger, config.Linger);

        if (config.TcpKeepalive)
        {
            socket.SetOption(SocketOption.Tcp_Keepalive, 1);
            socket.SetOption(SocketOption.Tcp_Keepalive_Idle, config.TcpKeepaliveIdle);
            socket.SetOption(SocketOption.Tcp_Keepalive_Intvl, config.TcpKeepaliveInterval);
        }
    }

    /// <summary>
    /// Configures client socket options (connect + send).
    /// </summary>
    private void ConfigureClientSocket(Socket socket, PlaySocketConfig config)
    {
        socket.SetOption(SocketOption.Routing_Id, _serverIdBytes);
        socket.SetOption(SocketOption.Router_Handover, 1);
        socket.SetOption(SocketOption.Router_Mandatory, 1);
        socket.SetOption(SocketOption.Immediate, 0); // DelayAttachOnConnect equivalent
        socket.SetOption(SocketOption.Sndhwm, config.SendHighWatermark);
        socket.SetOption(SocketOption.Linger, config.Linger);

        if (config.TcpKeepalive)
        {
            socket.SetOption(SocketOption.Tcp_Keepalive, 1);
            socket.SetOption(SocketOption.Tcp_Keepalive_Idle, config.TcpKeepaliveIdle);
            socket.SetOption(SocketOption.Tcp_Keepalive_Intvl, config.TcpKeepaliveInterval);
        }
    }

    /// <summary>
    /// Creates a ZmqPlaySocket from ServerConfig.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <param name="context">Optional ZMQ context (creates new if null).</param>
    /// <returns>A new ZmqPlaySocket instance.</returns>
    public static ZmqPlaySocket Create(ServerConfig config, Context? context = null)
    {
        var socketConfig = PlaySocketConfig.FromServerConfig(config);
        return new ZmqPlaySocket(config.ServerId, config.BindEndpoint, socketConfig, context);
    }

    /// <inheritdoc/>
    public void Bind()
    {
        ThrowIfDisposed();
        _serverSocket.Bind(_bindEndpoint);
    }

    /// <inheritdoc/>
    public void Connect(string endpoint)
    {
        ThrowIfDisposed();
        _clientSocket.Connect(endpoint);
    }

    /// <inheritdoc/>
    public void Disconnect(string endpoint)
    {
        ThrowIfDisposed();
        _clientSocket.Disconnect(endpoint);
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
                // Frame 0: Target ServerId (SendMore)
                _clientSocket.Send(Encoding.UTF8.GetBytes(serverId), SendFlags.SendMore);

                // Frame 1: RouteHeader (SendMore)
                var headerBytes = packet.SerializeHeader();
                _clientSocket.Send(headerBytes, SendFlags.SendMore);

                // Frame 2: Payload (마지막 프레임) - Zero-copy with ReadOnlySpan
                _clientSocket.Send(packet.Payload.DataSpan);
            }
            catch (Exception ex)
            {
                // RouterMandatory causes send to fail if target not connected
                Console.Error.WriteLine($"[ZmqPlaySocket] Failed to send to {serverId}, MsgId: {packet.MsgId}, Error: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public RoutePacket? Receive()
    {
        ThrowIfDisposed();

        // Frame 0: Sender ServerId (고정 버퍼)
        int serverIdLen = _serverSocket.Recv(_recvServerIdBuffer);
        if (serverIdLen <= 0)
        {
            return null;
        }

        var senderServerId = Encoding.UTF8.GetString(_recvServerIdBuffer, 0, serverIdLen);

        // Frame 1: RouteHeader (고정 버퍼)
        int headerLen = _serverSocket.Recv(_recvHeaderBuffer);
        if (headerLen <= 0)
        {
            return null;
        }

        // Frame 2: Payload (Message로 수신 - dispose 하지 않음, RoutePacket이 관리)
        var payloadMessage = new Net.Zmq.Message();
        _serverSocket.Recv(payloadMessage);

        // RoutePacket 생성 (Message 수명 관리 위임)
        return RoutePacket.FromFrames(
            _recvHeaderBuffer.AsSpan(0, headerLen),
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
        _serverSocket.Dispose();
        _clientSocket.Dispose();

        // Only dispose context if we own it (created it ourselves)
        if (_ownsContext)
        {
            _context.Dispose();
        }
    }
}
