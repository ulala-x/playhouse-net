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
/// Uses Router-Router pattern for bidirectional messaging.
/// The socket identity is set to the NID for routing purposes.
/// Follows Kairos pattern with multipart message handling.
/// </remarks>
internal sealed class NetMqPlaySocket : IPlaySocket
{
    private readonly string _bindEndpoint;
    private readonly RouterSocket _socket;
    private bool _disposed;

    /// <inheritdoc/>
    public string Nid { get; }

    /// <inheritdoc/>
    public string EndPoint => _bindEndpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetMqPlaySocket"/> class.
    /// </summary>
    /// <param name="nid">Node ID for this socket.</param>
    /// <param name="bindEndpoint">Bind endpoint address (e.g., "tcp://*:5555").</param>
    /// <param name="config">Socket configuration.</param>
    public NetMqPlaySocket(string nid, string bindEndpoint, PlaySocketConfig? config = null)
    {
        Nid = nid;
        _bindEndpoint = bindEndpoint;
        config ??= PlaySocketConfig.Default;

        _socket = new RouterSocket();

        // Set socket identity to NID for routing
        _socket.Options.Identity = Encoding.UTF8.GetBytes(nid);

        // Router options
        _socket.Options.DelayAttachOnConnect = true; // immediate
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
        var socketConfig = PlaySocketConfig.FromServerConfig(config);
        return new NetMqPlaySocket(config.Nid, config.BindEndpoint, socketConfig);
    }

    /// <inheritdoc/>
    public void Bind()
    {
        ThrowIfDisposed();
        _socket.Bind(_bindEndpoint);
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
    public void Send(string nid, RuntimeRoutePacket packet)
    {
        ThrowIfDisposed();

        // Note: No lock needed - Action queue in XClientCommunicator provides serialization
        using (packet)
        {
            var message = new NetMQMessage();

            // Frame 0: Target NID
            message.Append(new NetMQFrame(Encoding.UTF8.GetBytes(nid)));

            // Frame 1: RouteHeader
            var headerBytes = packet.SerializeHeader();
            message.Append(new NetMQFrame(headerBytes));

            // Frame 2: Payload
            var payloadBytes = packet.GetPayloadBytes();
            message.Append(new NetMQFrame(payloadBytes));

            if (!_socket.TrySendMultipartMessage(message))
            {
                // RouterMandatory causes send to fail if target not connected
                // Log error or throw depending on requirements
                Console.Error.WriteLine($"[NetMqPlaySocket] Failed to send to {nid}, MsgId: {packet.MsgId}");
            }
        }
    }

    /// <inheritdoc/>
    public RuntimeRoutePacket? Receive()
    {
        ThrowIfDisposed();

        var message = new NetMQMessage();
        if (_socket.TryReceiveMultipartMessage(TimeSpan.FromSeconds(1), ref message))
        {
            if (message.FrameCount < 3)
            {
                Console.Error.WriteLine($"[NetMqPlaySocket] Invalid message frame count: {message.FrameCount}");
                return null;
            }

            // Frame 0: Sender NID
            var senderNid = Encoding.UTF8.GetString(message[0].Buffer);

            // Frame 1: RouteHeader
            var headerBytes = message[1].ToByteArray();

            // Frame 2: Payload
            var payloadBytes = message[2].ToByteArray();

            // Create packet with sender NID set in header (Kairos pattern)
            return RuntimeRoutePacket.FromFrames(headerBytes, payloadBytes, senderNid);
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
        _socket.Dispose();
    }
}
