#nullable enable

namespace PlayHouse.Runtime.ServerMesh;

/// <summary>
/// Configuration for a PlayHouse server instance.
/// </summary>
/// <remarks>
/// NID (Node ID) format: "{ServiceId}:{ServerId}"
/// Example: "1:1" for Play Server #1, "2:1" for API Server #1
/// </remarks>
public sealed class ServerConfig
{
    /// <summary>
    /// Gets the service identifier (1 = Play, 2 = API, etc.).
    /// </summary>
    public ushort ServiceId { get; }

    /// <summary>
    /// Gets the server instance identifier (unique string, e.g., "play-1", "api-seoul-1").
    /// </summary>
    public string ServerId { get; }

    /// <summary>
    /// Gets the bind address for ZMQ socket (e.g., "tcp://*:5555").
    /// </summary>
    public string BindAddress { get; }

    /// <summary>
    /// Gets the bind endpoint for ZMQ socket (alias for BindAddress).
    /// </summary>
    public string BindEndpoint => BindAddress;

    /// <summary>
    /// Gets the request timeout in milliseconds (default: 30000ms).
    /// </summary>
    public int RequestTimeoutMs { get; }

    /// <summary>
    /// Gets the high water mark for outgoing messages.
    /// </summary>
    public int SendHighWatermark { get; }

    /// <summary>
    /// Gets the high water mark for incoming messages.
    /// </summary>
    public int ReceiveHighWatermark { get; }

    /// <summary>
    /// Gets whether TCP keepalive is enabled.
    /// </summary>
    public bool TcpKeepalive { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConfig"/> class.
    /// </summary>
    /// <param name="serviceId">Service identifier.</param>
    /// <param name="serverId">Server instance identifier.</param>
    /// <param name="bindAddress">ZMQ bind address.</param>
    /// <param name="requestTimeoutMs">Request timeout in milliseconds.</param>
    /// <param name="sendHighWatermark">Send high water mark.</param>
    /// <param name="receiveHighWatermark">Receive high water mark.</param>
    /// <param name="tcpKeepalive">Enable TCP keepalive.</param>
    public ServerConfig(
        ushort serviceId,
        string serverId,
        string bindAddress,
        int requestTimeoutMs = 30000,
        int sendHighWatermark = 1000,
        int receiveHighWatermark = 1000,
        bool tcpKeepalive = true)
    {
        ServiceId = serviceId;
        ServerId = serverId;
        BindAddress = bindAddress;
        RequestTimeoutMs = requestTimeoutMs;
        SendHighWatermark = sendHighWatermark;
        ReceiveHighWatermark = receiveHighWatermark;
        TcpKeepalive = tcpKeepalive;
    }

    /// <summary>
    /// Creates a ServerConfig with default settings.
    /// </summary>
    /// <param name="serviceId">Service identifier.</param>
    /// <param name="serverId">Server instance identifier.</param>
    /// <param name="port">Port number for binding.</param>
    /// <returns>A new ServerConfig instance.</returns>
    public static ServerConfig Create(ushort serviceId, string serverId, int port)
    {
        return new ServerConfig(
            serviceId,
            serverId,
            $"tcp://*:{port}");
    }

    /// <summary>
    /// Gets the connect address for a remote server.
    /// </summary>
    /// <param name="host">Remote host address.</param>
    /// <param name="port">Remote port number.</param>
    /// <returns>Connect address string.</returns>
    public static string GetConnectAddress(string host, int port)
    {
        return $"tcp://{host}:{port}";
    }
}

/// <summary>
/// Well-known service identifiers.
/// </summary>
public static class ServiceIds
{
    /// <summary>
    /// Play server service ID.
    /// </summary>
    public const ushort Play = 1;

    /// <summary>
    /// API server service ID.
    /// </summary>
    public const ushort Api = 2;
}
