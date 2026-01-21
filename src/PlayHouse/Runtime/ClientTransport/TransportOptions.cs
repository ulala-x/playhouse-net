#nullable enable

using System.Security.Cryptography.X509Certificates;

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Configuration options for transport sessions.
/// </summary>
public sealed class TransportOptions
{
    /// <summary>
    /// Size of the receive buffer in bytes. Default: 64 KB.
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 64 * 1024;

    /// <summary>
    /// Size of the send buffer in bytes. Default: 64 KB.
    /// </summary>
    public int SendBufferSize { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum allowed packet size in bytes. Default: 2 MB.
    /// Packets exceeding this size will be rejected.
    /// </summary>
    public int MaxPacketSize { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// Timeout for heartbeat response. Default: 90 seconds.
    /// Session will be closed if no data is received within this time.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Threshold at which the writer will pause to prevent buffering too much data.
    /// Default: 64 KB.
    /// </summary>
    public int PauseWriterThreshold { get; set; } = 64 * 1024;

    /// <summary>
    /// Threshold at which the paused writer will resume.
    /// Default: 32 KB.
    /// </summary>
    public int ResumeWriterThreshold { get; set; } = 32 * 1024;

    /// <summary>
    /// Whether to enable TCP keep-alive. Default: true.
    /// Only applicable to TCP transport.
    /// </summary>
    public bool EnableKeepAlive { get; set; } = true;

    /// <summary>
    /// TCP keep-alive time in seconds. Default: 60 seconds.
    /// </summary>
    public int KeepAliveTime { get; set; } = 60;

    /// <summary>
    /// TCP keep-alive interval in seconds. Default: 1 second.
    /// </summary>
    public int KeepAliveInterval { get; set; } = 1;
}

/// <summary>
/// TLS configuration options.
/// </summary>
public sealed class TlsOptions
{
    /// <summary>
    /// Whether TLS is enabled. Default: false.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The server certificate for TLS.
    /// </summary>
    public X509Certificate2? Certificate { get; init; }

    /// <summary>
    /// Whether to require client certificate. Default: false.
    /// </summary>
    public bool RequireClientCertificate { get; init; }

    /// <summary>
    /// Whether to check certificate revocation. Default: true.
    /// </summary>
    public bool CheckCertificateRevocation { get; init; } = true;
}
