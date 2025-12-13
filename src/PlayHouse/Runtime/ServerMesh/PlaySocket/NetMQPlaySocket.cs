#nullable enable

using System.Text;
using Google.Protobuf;
using NetMQ;
using NetMQ.Sockets;
using PlayHouse.Runtime.Proto;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Runtime.ServerMesh.PlaySocket;

/// <summary>
/// NetMQ Router socket implementation for server-to-server communication.
/// </summary>
/// <remarks>
/// Uses dual Router sockets for bidirectional messaging:
/// - Server socket: Bind and Receive
/// - Client socket: Connect and Send
/// This separation allows self-messaging (sending to own server socket).
/// </remarks>
internal sealed class NetMqPlaySocket : IPlaySocket
{
    private readonly string _bindEndpoint;
    private readonly RouterSocket _serverSocket;  // For Bind + Receive
    private readonly RouterSocket _clientSocket;  // For Connect + Send
    private bool _disposed;

    /// <inheritdoc/>
    public string ServerId { get; }

    /// <inheritdoc/>
    public string EndPoint => _bindEndpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMqPlaySocket"/> class.
    /// </summary>
    /// <param name="serverId">Server ID for this socket.</param>
    /// <param name="bindEndpoint">Bind endpoint address (e.g., "tcp://*:5555").</param>
    /// <param name="config">Socket configuration.</param>
    public NetMqPlaySocket(string serverId, string bindEndpoint, PlaySocketConfig? config = null)
    {
        ServerId = serverId;
        _bindEndpoint = bindEndpoint;
        config ??= PlaySocketConfig.Default;

        // Server socket - for receiving messages
        _serverSocket = new RouterSocket();
        _serverSocket.Options.Identity = Encoding.UTF8.GetBytes(serverId);
        _serverSocket.Options.RouterHandover = true;
        _serverSocket.Options.ReceiveHighWatermark = config.ReceiveHighWatermark;
        _serverSocket.Options.TcpKeepalive = config.TcpKeepalive;
        if (config.TcpKeepalive)
        {
            _serverSocket.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(config.TcpKeepaliveIdle);
            _serverSocket.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(config.TcpKeepaliveInterval);
        }
        _serverSocket.Options.Linger = TimeSpan.FromMilliseconds(config.Linger);

        // Client socket - for sending messages
        _clientSocket = new RouterSocket();
        _clientSocket.Options.Identity = Encoding.UTF8.GetBytes(serverId);
        _clientSocket.Options.DelayAttachOnConnect = true;
        _clientSocket.Options.RouterHandover = true;
        _clientSocket.Options.RouterMandatory = true;
        _clientSocket.Options.SendHighWatermark = config.SendHighWatermark;
        _clientSocket.Options.TcpKeepalive = config.TcpKeepalive;
        if (config.TcpKeepalive)
        {
            _clientSocket.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(config.TcpKeepaliveIdle);
            _clientSocket.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(config.TcpKeepaliveInterval);
        }
        _clientSocket.Options.Linger = TimeSpan.FromMilliseconds(config.Linger);
    }

    /// <summary>
    /// Creates a NetMQPlaySocket from ServerConfig.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <returns>A new NetMQPlaySocket instance.</returns>
    public static NetMqPlaySocket Create(ServerConfig config)
    {
        var socketConfig = PlaySocketConfig.FromServerConfig(config);
        return new NetMqPlaySocket(config.ServerId, config.BindEndpoint, socketConfig);
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
            var message = new NetMQMessage();

            // Frame 0: Target ServerId
            message.Append(new NetMQFrame(Encoding.UTF8.GetBytes(serverId)));

            // Frame 1: RouteHeader
            var headerBytes = packet.SerializeHeader();
            message.Append(new NetMQFrame(headerBytes));

            // Frame 2: Payload
            var payloadBytes = packet.GetPayloadBytes();
            message.Append(new NetMQFrame(payloadBytes));

            if (!_clientSocket.TrySendMultipartMessage(message))
            {
                // RouterMandatory causes send to fail if target not connected
                // Log error or throw depending on requirements
                Console.Error.WriteLine($"[NetMqPlaySocket] Failed to send to {serverId}, MsgId: {packet.MsgId}");
            }
        }
    }

    /// <inheritdoc/>
    public RuntimeRoutePacket? Receive()
    {
        ThrowIfDisposed();

        var message = new NetMQMessage();
        if (_serverSocket.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(10), ref message))
        {
            if (message.FrameCount < 3)
            {
                Console.Error.WriteLine($"[NetMqPlaySocket] Invalid message frame count: {message.FrameCount}");
                return null;
            }

            // Frame 0: Sender ServerId
            var senderServerId = Encoding.UTF8.GetString(message[0].Buffer);

            // Frame 1: RouteHeader
            var headerBytes = message[1].ToByteArray();

            // Frame 2: Payload
            var payloadBytes = message[2].ToByteArray();

            // Create packet with sender ServerId set in header (Kairos pattern)
            return RuntimeRoutePacket.FromFrames(headerBytes, payloadBytes, senderServerId);
        }

        return null;
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
        _serverSocket.Dispose();
        _clientSocket.Dispose();
    }
}
