namespace PlayHouse.Connector;

/// <summary>
/// Configuration options for PlayHouseClient.
/// </summary>
public sealed class PlayHouseClientOptions
{
    /// <summary>
    /// Default request timeout (30 seconds).
    /// </summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default connection timeout (10 seconds).
    /// </summary>
    public static readonly TimeSpan DefaultConnectionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Default heartbeat interval (30 seconds).
    /// </summary>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the default timeout for request operations.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = DefaultRequestTimeout;

    /// <summary>
    /// Gets or sets the connection timeout.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = DefaultConnectionTimeout;

    /// <summary>
    /// Gets or sets the heartbeat interval for keeping connections alive.
    /// Default: 30 seconds. Set to TimeSpan.Zero to disable heartbeat.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = DefaultHeartbeatInterval;

    /// <summary>
    /// Gets or sets whether to automatically reconnect on connection loss.
    /// Default: false.
    /// </summary>
    public bool AutoReconnect { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts.
    /// Default: 3. Only applies when AutoReconnect is true.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between reconnection attempts.
    /// Default: 2 seconds. Uses exponential backoff if ReconnectBackoffMultiplier > 1.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the backoff multiplier for reconnection delays.
    /// Default: 2.0 (exponential backoff). Set to 1.0 for constant delay.
    /// </summary>
    public double ReconnectBackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the maximum reconnection delay when using backoff.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the TCP send buffer size in bytes.
    /// Default: 8192 (8 KB).
    /// </summary>
    public int SendBufferSize { get; set; } = 8192;

    /// <summary>
    /// Gets or sets the TCP receive buffer size in bytes.
    /// Default: 8192 (8 KB).
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 8192;

    /// <summary>
    /// Gets or sets whether to enable TCP keep-alive.
    /// Default: true.
    /// </summary>
    public bool TcpKeepAlive { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to disable Nagle's algorithm (set TCP_NODELAY).
    /// Default: true (Nagle disabled for lower latency).
    /// </summary>
    public bool TcpNoDelay { get; set; } = true;

    /// <summary>
    /// Gets or sets the WebSocket sub-protocol to use.
    /// Default: null (no sub-protocol).
    /// </summary>
    public string? WebSocketSubProtocol { get; set; }

    /// <summary>
    /// Gets or sets whether to enable message compression.
    /// Default: false.
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum message size in bytes.
    /// Default: 1 MB.
    /// </summary>
    public int MaxMessageSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Validates the options and throws if invalid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when options are invalid</exception>
    public void Validate()
    {
        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("RequestTimeout must be greater than zero.", nameof(RequestTimeout));
        }

        if (ConnectionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("ConnectionTimeout must be greater than zero.", nameof(ConnectionTimeout));
        }

        if (HeartbeatInterval < TimeSpan.Zero)
        {
            throw new ArgumentException("HeartbeatInterval cannot be negative.", nameof(HeartbeatInterval));
        }

        if (MaxReconnectAttempts < 0)
        {
            throw new ArgumentException("MaxReconnectAttempts cannot be negative.", nameof(MaxReconnectAttempts));
        }

        if (ReconnectDelay < TimeSpan.Zero)
        {
            throw new ArgumentException("ReconnectDelay cannot be negative.", nameof(ReconnectDelay));
        }

        if (ReconnectBackoffMultiplier < 1.0)
        {
            throw new ArgumentException("ReconnectBackoffMultiplier must be at least 1.0.", nameof(ReconnectBackoffMultiplier));
        }

        if (MaxReconnectDelay < TimeSpan.Zero)
        {
            throw new ArgumentException("MaxReconnectDelay cannot be negative.", nameof(MaxReconnectDelay));
        }

        if (SendBufferSize <= 0)
        {
            throw new ArgumentException("SendBufferSize must be greater than zero.", nameof(SendBufferSize));
        }

        if (ReceiveBufferSize <= 0)
        {
            throw new ArgumentException("ReceiveBufferSize must be greater than zero.", nameof(ReceiveBufferSize));
        }

        if (MaxMessageSize <= 0)
        {
            throw new ArgumentException("MaxMessageSize must be greater than zero.", nameof(MaxMessageSize));
        }
    }
}
