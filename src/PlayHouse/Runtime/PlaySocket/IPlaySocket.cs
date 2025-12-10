#nullable enable

namespace PlayHouse.Runtime.PlaySocket;

/// <summary>
/// Interface for NetMQ-based server-to-server communication sockets.
/// </summary>
/// <remarks>
/// Implements Router-Router pattern for bidirectional messaging.
/// 3-Frame message structure:
/// - Frame 0: Target NID (UTF-8)
/// - Frame 1: RouteHeader (Protobuf serialized)
/// - Frame 2: Payload (binary)
/// </remarks>
public interface IPlaySocket : IDisposable
{
    /// <summary>
    /// Gets the Node ID of this socket.
    /// </summary>
    string Nid { get; }

    /// <summary>
    /// Gets whether the socket is bound or connected.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Binds the socket to the specified address for receiving messages.
    /// </summary>
    /// <param name="address">Bind address (e.g., "tcp://*:5555").</param>
    void Bind(string address);

    /// <summary>
    /// Connects to a remote socket.
    /// </summary>
    /// <param name="address">Connect address (e.g., "tcp://localhost:5555").</param>
    void Connect(string address);

    /// <summary>
    /// Disconnects from a remote socket.
    /// </summary>
    /// <param name="address">Address to disconnect from.</param>
    void Disconnect(string address);

    /// <summary>
    /// Sends a message to the specified target.
    /// </summary>
    /// <param name="targetNid">Target node ID.</param>
    /// <param name="headerBytes">RouteHeader serialized bytes.</param>
    /// <param name="payload">Payload bytes.</param>
    /// <returns>True if sent successfully, false otherwise.</returns>
    bool Send(string targetNid, ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Tries to receive a message (non-blocking).
    /// </summary>
    /// <param name="senderNid">Sender's node ID.</param>
    /// <param name="headerBytes">RouteHeader bytes.</param>
    /// <param name="payload">Payload bytes.</param>
    /// <returns>True if a message was received, false otherwise.</returns>
    bool TryReceive(out string senderNid, out byte[] headerBytes, out byte[] payload);

    /// <summary>
    /// Receives a message (blocking with timeout).
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds (-1 for infinite).</param>
    /// <param name="senderNid">Sender's node ID.</param>
    /// <param name="headerBytes">RouteHeader bytes.</param>
    /// <param name="payload">Payload bytes.</param>
    /// <returns>True if a message was received, false if timed out.</returns>
    bool Receive(int timeoutMs, out string senderNid, out byte[] headerBytes, out byte[] payload);
}

/// <summary>
/// Configuration options for PlaySocket.
/// </summary>
public sealed class PlaySocketConfig
{
    /// <summary>
    /// Gets or sets the send high water mark.
    /// </summary>
    public int SendHighWatermark { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the receive high water mark.
    /// </summary>
    public int ReceiveHighWatermark { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether to enable TCP keepalive.
    /// </summary>
    public bool TcpKeepalive { get; set; } = true;

    /// <summary>
    /// Gets or sets the TCP keepalive idle time in seconds.
    /// </summary>
    public int TcpKeepaliveIdle { get; set; } = 60;

    /// <summary>
    /// Gets or sets the TCP keepalive interval in seconds.
    /// </summary>
    public int TcpKeepaliveInterval { get; set; } = 10;

    /// <summary>
    /// Gets or sets the linger time in milliseconds.
    /// </summary>
    public int Linger { get; set; } = 0;

    /// <summary>
    /// Creates a default configuration.
    /// </summary>
    public static PlaySocketConfig Default => new();

    /// <summary>
    /// Creates configuration from ServerConfig.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <returns>Socket configuration.</returns>
    public static PlaySocketConfig FromServerConfig(ServerConfig config) => new()
    {
        SendHighWatermark = config.SendHighWatermark,
        ReceiveHighWatermark = config.ReceiveHighWatermark,
        TcpKeepalive = config.TcpKeepalive
    };
}
