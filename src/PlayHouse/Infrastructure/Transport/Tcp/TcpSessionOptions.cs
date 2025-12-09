#nullable enable

namespace PlayHouse.Infrastructure.Transport.Tcp;

/// <summary>
/// Configuration options for TCP sessions.
/// </summary>
public sealed class TcpSessionOptions
{
    /// <summary>
    /// Size of the receive buffer in bytes. Default: 64 KB.
    /// </summary>
    public int ReceiveBufferSize { get; init; } = 64 * 1024;

    /// <summary>
    /// Size of the send buffer in bytes. Default: 64 KB.
    /// </summary>
    public int SendBufferSize { get; init; } = 64 * 1024;

    /// <summary>
    /// Maximum allowed packet size in bytes. Default: 2 MB.
    /// Packets exceeding this size will be rejected.
    /// </summary>
    public int MaxPacketSize { get; init; } = 2 * 1024 * 1024;

    /// <summary>
    /// Interval between heartbeat messages. Default: 30 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for heartbeat response. Default: 90 seconds.
    /// Session will be closed if no heartbeat is received within this time.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Threshold at which the writer will pause to prevent buffering too much data.
    /// Default: 64 KB.
    /// </summary>
    public int PauseWriterThreshold { get; init; } = 64 * 1024;

    /// <summary>
    /// Threshold at which the paused writer will resume.
    /// Default: 32 KB.
    /// </summary>
    public int ResumeWriterThreshold { get; init; } = 32 * 1024;

    /// <summary>
    /// Whether to enable TCP keep-alive. Default: true.
    /// </summary>
    public bool EnableKeepAlive { get; init; } = true;

    /// <summary>
    /// TCP keep-alive time in seconds. Default: 7200 seconds (2 hours).
    /// </summary>
    public int KeepAliveTime { get; init; } = 7200;

    /// <summary>
    /// TCP keep-alive interval in seconds. Default: 1 second.
    /// </summary>
    public int KeepAliveInterval { get; init; } = 1;
}
