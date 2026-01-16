#nullable enable

using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using PlayHouse.Runtime.ClientTransport.Tcp;
using PlayHouse.Runtime.ClientTransport.WebSocket;

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Builder for creating transport servers with various configurations.
/// </summary>
/// <remarks>
/// Supports TCP, WebSocket, and combinations with optional SSL/TLS.
/// </remarks>
public sealed class TransportServerBuilder
{
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger _logger;

    private readonly List<Action<CompositeTransportServer>> _serverFactories = new();
    private TransportOptions _options = new();

    /// <summary>
    /// Creates a new transport server builder.
    /// </summary>
    /// <param name="onMessage">Message received callback.</param>
    /// <param name="onDisconnect">Session disconnected callback.</param>
    /// <param name="logger">Logger instance.</param>
    public TransportServerBuilder(
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger logger)
    {
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
    }

    /// <summary>
    /// Configures transport options.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    /// <returns>This builder for chaining.</returns>
    public TransportServerBuilder WithOptions(Action<TransportOptions> configure)
    {
        var options = new TransportOptions();
        configure(options);
        _options = options;
        return this;
    }

    /// <summary>
    /// Adds a TCP server.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="bindAddress">The address to bind to (default: any).</param>
    /// <returns>This builder for chaining.</returns>
    public TransportServerBuilder AddTcp(int port, string? bindAddress = null)
    {
        var endpoint = new IPEndPoint(
            string.IsNullOrEmpty(bindAddress) ? IPAddress.Any : IPAddress.Parse(bindAddress),
            port);

        _serverFactories.Add(composite =>
        {
            var server = new TcpTransportServer(endpoint, _options, null, _onMessage, _onDisconnect, _logger);
            composite.Add(server);
        });

        return this;
    }

    /// <summary>
    /// Adds a TCP server with SSL/TLS.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="certificate">The server certificate.</param>
    /// <param name="bindAddress">The address to bind to (default: any).</param>
    /// <returns>This builder for chaining.</returns>
    public TransportServerBuilder AddTcpWithSsl(int port, X509Certificate2 certificate, string? bindAddress = null)
    {
        var endpoint = new IPEndPoint(
            string.IsNullOrEmpty(bindAddress) ? IPAddress.Any : IPAddress.Parse(bindAddress),
            port);

        var sslOptions = new SslOptions
        {
            Enabled = true,
            Certificate = certificate
        };

        _serverFactories.Add(composite =>
        {
            var server = new TcpTransportServer(endpoint, _options, sslOptions, _onMessage, _onDisconnect, _logger);
            composite.Add(server);
        });

        return this;
    }

    /// <summary>
    /// Adds a WebSocket server.
    /// </summary>
    /// <param name="path">The URL path to handle (e.g., "/ws").</param>
    /// <returns>This builder for chaining.</returns>
    /// <remarks>
    /// For HTTPS/WSS, configure HTTPS in your ASP.NET Core application.
    /// The WebSocket server will automatically use WSS when HTTPS is enabled.
    /// </remarks>
    public TransportServerBuilder AddWebSocket(string path = "/ws")
    {
        _serverFactories.Add(composite =>
        {
            var server = new WebSocketTransportServer(path, _options, _onMessage, _onDisconnect, _logger);
            composite.Add(server);
        });

        return this;
    }

    /// <summary>
    /// Builds the transport server.
    /// </summary>
    /// <returns>The configured transport server.</returns>
    public ITransportServer Build()
    {
        if (_serverFactories.Count == 0)
        {
            throw new InvalidOperationException("At least one transport must be configured");
        }

        if (_serverFactories.Count == 1)
        {
            // Single server, no need for composite
            var composite = new CompositeTransportServer(_logger);
            _serverFactories[0](composite);
            // Return the first (and only) server directly
            // But we still need the composite for unified interface
            return composite;
        }

        var result = new CompositeTransportServer(_logger);
        foreach (var factory in _serverFactories)
        {
            factory(result);
        }
        return result;
    }
}

/// <summary>
/// Transport type enumeration for configuration.
/// </summary>
[Flags]
public enum TransportType
{
    /// <summary>
    /// No transport configured.
    /// </summary>
    None = 0,

    /// <summary>
    /// TCP transport.
    /// </summary>
    Tcp = 1,

    /// <summary>
    /// TCP with SSL/TLS transport.
    /// </summary>
    TcpSsl = 2,

    /// <summary>
    /// WebSocket transport (WS).
    /// </summary>
    WebSocket = 4,

    /// <summary>
    /// WebSocket with SSL/TLS transport (WSS).
    /// Requires HTTPS configuration in ASP.NET Core.
    /// </summary>
    WebSocketSsl = 8,

    /// <summary>
    /// All transport types.
    /// </summary>
    All = Tcp | TcpSsl | WebSocket | WebSocketSsl
}
