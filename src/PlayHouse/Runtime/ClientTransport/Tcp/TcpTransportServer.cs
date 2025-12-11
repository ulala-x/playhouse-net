#nullable enable

using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Runtime.ClientTransport.Tcp;

/// <summary>
/// TCP transport server using System.IO.Pipelines for efficient I/O.
/// </summary>
/// <remarks>
/// Supports optional SSL/TLS encryption.
/// </remarks>
public sealed class TcpTransportServer : ITransportServer
{
    private readonly IPEndPoint _endpoint;
    private readonly TransportOptions _options;
    private readonly SslOptions? _sslOptions;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger? _logger;

    private readonly ConcurrentDictionary<long, TcpTransportSession> _sessions = new();
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    private Task? _acceptTask;
    private long _nextSessionId;
    private bool _disposed;

    public int SessionCount => _sessions.Count;

    /// <summary>
    /// Gets the actual port the server is bound to.
    /// Returns 0 if the server has not started yet.
    /// </summary>
    public int ActualPort => _listener.LocalEndpoint is IPEndPoint ep ? ep.Port : 0;

    /// <summary>
    /// Creates a new TCP transport server.
    /// </summary>
    /// <param name="endpoint">The endpoint to listen on.</param>
    /// <param name="options">Transport options.</param>
    /// <param name="sslOptions">SSL options (null for no SSL).</param>
    /// <param name="onMessage">Message received callback.</param>
    /// <param name="onDisconnect">Session disconnected callback.</param>
    /// <param name="logger">Logger instance.</param>
    public TcpTransportServer(
        IPEndPoint endpoint,
        TransportOptions options,
        SslOptions? sslOptions,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger? logger = null)
    {
        _endpoint = endpoint;
        _options = options;
        _sslOptions = sslOptions;
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
        _listener = new TcpListener(endpoint);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _listener.Start();

        var protocol = _sslOptions?.Enabled == true ? "TCP+TLS" : "TCP";
        _logger?.LogInformation("{Protocol} server started on {Endpoint}", protocol, _endpoint);

        _acceptTask = AcceptLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_disposed) return;

        _logger?.LogInformation("Stopping TCP server on {Endpoint}", _endpoint);

        _listener.Stop();
        _cts.Cancel();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException) { }
        }

        await DisconnectAllSessionsAsync();
    }

    public ITransportSession? GetSession(long sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public async ValueTask DisconnectSessionAsync(long sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            await session.DisconnectAsync();
        }
    }

    public async Task DisconnectAllSessionsAsync()
    {
        var tasks = _sessions.Values.Select(s => s.DisconnectAsync().AsTask());
        await Task.WhenAll(tasks);
        _sessions.Clear();
    }

    public IEnumerable<ITransportSession> GetAllSessions()
    {
        return _sessions.Values;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var socket = await _listener.AcceptSocketAsync(ct);
                var sessionId = Interlocked.Increment(ref _nextSessionId);

                _ = Task.Run(async () => await HandleClientAsync(sessionId, socket, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in accept loop");
        }
    }

    private async Task HandleClientAsync(long sessionId, Socket socket, CancellationToken ct)
    {
        Stream stream = new NetworkStream(socket, ownsSocket: false);

        try
        {
            // Apply SSL/TLS if configured
            if (_sslOptions?.Enabled == true && _sslOptions.Certificate != null)
            {
                var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);

                await sslStream.AuthenticateAsServerAsync(
                    _sslOptions.Certificate,
                    clientCertificateRequired: _sslOptions.RequireClientCertificate,
                    enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                    checkCertificateRevocation: _sslOptions.CheckCertificateRevocation);

                stream = sslStream;

                _logger?.LogDebug("SSL handshake completed for session {SessionId}", sessionId);
            }

            var session = new TcpTransportSession(
                sessionId,
                socket,
                stream,
                _options,
                _onMessage,
                OnSessionDisconnected,
                _logger,
                ct);

            if (_sessions.TryAdd(sessionId, session))
            {
                _logger?.LogDebug("TCP session {SessionId} accepted", sessionId);
            }
            else
            {
                await session.DisposeAsync();
            }
        }
        catch (AuthenticationException ex)
        {
            _logger?.LogWarning(ex, "SSL authentication failed for session {SessionId}", sessionId);
            stream.Dispose();
            socket.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling client {SessionId}", sessionId);
            stream.Dispose();
            socket.Dispose();
        }
    }

    private void OnSessionDisconnected(ITransportSession session, Exception? ex)
    {
        _sessions.TryRemove(session.SessionId, out _);
        _onDisconnect(session, ex);
    }
}
