#nullable enable

using System.Text;
using Net.Zmq;
using PlayHouse.Runtime.Proto;
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
    public void Send(string serverId, RuntimeRoutePacket packet)
    {
        ThrowIfDisposed();

        // Note: No lock needed - Action queue in XClientCommunicator provides serialization
        using (packet)
        {
            using var message = new MultipartMessage();

            // Frame 0: Target ServerId
            message.Add(Encoding.UTF8.GetBytes(serverId));

            // Frame 1: RouteHeader
            var headerBytes = packet.SerializeHeader();
            message.Add(headerBytes);

            // Frame 2: Payload
            var payloadBytes = packet.GetPayloadBytes();
            message.Add(payloadBytes);

            try
            {
                _clientSocket.SendMultipart(message);
            }
            catch (Exception ex)
            {
                // RouterMandatory causes send to fail if target not connected
                // Log error or throw depending on requirements
                Console.Error.WriteLine($"[ZmqPlaySocket] Failed to send to {serverId}, MsgId: {packet.MsgId}, Error: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public RuntimeRoutePacket? Receive()
    {
        ThrowIfDisposed();

        if (!_serverSocket.TryRecvMultipart(out var message) || message == null)
        {
            return null;
        }

        using (message)
        {
            if (message.Count < 3)
            {
                Console.Error.WriteLine($"[ZmqPlaySocket] Invalid message frame count: {message.Count}");
                return null;
            }

            // Frame 0: Sender ServerId
            var senderServerId = Encoding.UTF8.GetString(message[0].ToArray());

            // Frame 1: RouteHeader
            var headerBytes = message[1].ToArray();

            // Frame 2: Payload
            var payloadBytes = message[2].ToArray();

            // Create packet with sender ServerId set in header (Kairos pattern)
            return RuntimeRoutePacket.FromFrames(headerBytes, payloadBytes, senderServerId);
        }
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
