#nullable enable

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Infrastructure.Transport.Tcp;

/// <summary>
/// TCP server using System.Net.Sockets for accepting client connections.
/// Manages multiple concurrent TCP sessions.
/// </summary>
public sealed class TcpServer : IAsyncDisposable
{
    private readonly Socket _listenSocket;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger<TcpServer> _logger;
    private readonly Func<long, Socket, Task<TcpSession>> _sessionFactory;
    private readonly ConcurrentDictionary<long, TcpSession> _sessions;
    private readonly TcpSessionOptions _options;
    private long _nextSessionId;
    private Task? _acceptTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new TCP server.
    /// </summary>
    /// <param name="options">Session configuration options.</param>
    /// <param name="sessionFactory">Factory function for creating new sessions.</param>
    /// <param name="logger">Logger instance.</param>
    public TcpServer(
        TcpSessionOptions options,
        Func<long, Socket, Task<TcpSession>> sessionFactory,
        ILogger<TcpServer> logger)
    {
        _options = options;
        _sessionFactory = sessionFactory;
        _logger = logger;
        _cts = new CancellationTokenSource();
        _sessions = new ConcurrentDictionary<long, TcpSession>();
        _nextSessionId = 1;

        _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listenSocket.NoDelay = true;
    }

    /// <summary>
    /// Gets the number of active sessions.
    /// </summary>
    public int SessionCount => _sessions.Count;

    /// <summary>
    /// Starts listening for incoming connections on the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint to bind to.</param>
    /// <param name="backlog">The maximum length of the pending connections queue. Default: 100.</param>
    public async Task StartAsync(IPEndPoint endpoint, int backlog = 100)
    {
        if (_acceptTask != null)
        {
            throw new InvalidOperationException("Server is already started");
        }

        _listenSocket.Bind(endpoint);
        _listenSocket.Listen(backlog);

        _logger.LogInformation("TCP server listening on {Endpoint}", endpoint);

        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the server and closes all active sessions.
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogInformation("Stopping TCP server");

        _cts.Cancel();

        // Wait for accept loop to complete
        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        // Close all sessions
        var disposeTasks = _sessions.Values.Select(session => session.DisposeAsync().AsTask());
        await Task.WhenAll(disposeTasks);

        _sessions.Clear();

        _logger.LogInformation("TCP server stopped");
    }

    /// <summary>
    /// Disposes the server and releases all resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopAsync();

        _listenSocket.Close();
        _listenSocket.Dispose();
        _cts.Dispose();

        _logger.LogInformation("TCP server disposed");
    }

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The session if found; otherwise, null.</returns>
    public TcpSession? GetSession(long sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Disconnects a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    public async Task DisconnectSessionAsync(long sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            await session.DisconnectAsync();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var clientSocket = await _listenSocket.AcceptAsync(cancellationToken);

                var sessionId = Interlocked.Increment(ref _nextSessionId);

                _logger.LogInformation("Accepted new connection: session {SessionId} from {RemoteEndPoint}",
                    sessionId, clientSocket.RemoteEndPoint);

                // Create session using factory
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var session = await _sessionFactory(sessionId, clientSocket);

                        if (_sessions.TryAdd(sessionId, session))
                        {
                            await session.StartAsync();
                        }
                        else
                        {
                            _logger.LogError("Failed to add session {SessionId} to dictionary", sessionId);
                            await session.DisposeAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating session {SessionId}", sessionId);
                        clientSocket.Close();
                        clientSocket.Dispose();
                    }
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in accept loop");
        }
    }

    internal void RemoveSession(long sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _logger.LogInformation("Session {SessionId} removed", sessionId);
    }
}
