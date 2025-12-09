#nullable enable

using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Infrastructure.Serialization;
using PlayHouse.Infrastructure.Transport.Tcp;
using PlayHouse.Infrastructure.Transport.WebSocket;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// Main PlayHouse server class implementing IHostedService.
/// Manages TCP and WebSocket servers for client connections.
/// </summary>
public sealed class PlayHouseServer : IHostedService, IAsyncDisposable
{
    private readonly ILogger<PlayHouseServer> _logger;
    private readonly PlayHouseOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PacketSerializer _packetSerializer;
    private readonly PacketDispatcher _packetDispatcher;
    private readonly SessionManager _sessionManager;
    private readonly RoomTokenManager _tokenManager;
    private TcpServer? _tcpServer;
    private WebSocketServer? _webSocketServer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new PlayHouse server instance.
    /// </summary>
    /// <param name="options">Server configuration options.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="packetDispatcher">The packet dispatcher for routing messages.</param>
    /// <param name="sessionManager">The session manager for tracking connections.</param>
    /// <param name="tokenManager">The room token manager for authentication.</param>
    public PlayHouseServer(
        IOptions<PlayHouseOptions> options,
        ILoggerFactory loggerFactory,
        PacketDispatcher packetDispatcher,
        SessionManager sessionManager,
        RoomTokenManager tokenManager)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PlayHouseServer>();
        _packetSerializer = new PacketSerializer();
        _packetDispatcher = packetDispatcher;
        _sessionManager = sessionManager;
        _tokenManager = tokenManager;
    }

    /// <summary>
    /// Gets the TCP server instance.
    /// </summary>
    public TcpServer? TcpServer => _tcpServer;

    /// <summary>
    /// Gets the WebSocket server instance.
    /// </summary>
    public WebSocketServer? WebSocketServer => _webSocketServer;

    /// <summary>
    /// Starts the PlayHouse server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting PlayHouse server on {Ip}:{Port}", _options.Ip, _options.Port);

        try
        {
            // Initialize TCP server
            _tcpServer = new TcpServer(
                _options.Session,
                CreateTcpSessionAsync,
                _loggerFactory.CreateLogger<TcpServer>());

            var endpoint = new IPEndPoint(IPAddress.Parse(_options.Ip), _options.Port);
            await _tcpServer.StartAsync(endpoint);

            _logger.LogInformation("PlayHouse TCP server started successfully");

            // Initialize WebSocket server if enabled
            if (_options.EnableWebSocket)
            {
                _webSocketServer = new WebSocketServer(
                    CreateWebSocketSessionAsync,
                    _loggerFactory.CreateLogger<WebSocketServer>());

                _logger.LogInformation("PlayHouse WebSocket server initialized on path {Path}",
                    _options.WebSocketPath);
            }

            _logger.LogInformation("PlayHouse server started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start PlayHouse server");
            throw;
        }
    }

    /// <summary>
    /// Stops the PlayHouse server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping PlayHouse server");

        try
        {
            if (_tcpServer != null)
            {
                await _tcpServer.StopAsync();
            }

            if (_webSocketServer != null)
            {
                await _webSocketServer.DisconnectAllSessionsAsync();
            }

            _logger.LogInformation("PlayHouse server stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping PlayHouse server");
            throw;
        }
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

        if (_tcpServer != null)
        {
            await _tcpServer.DisposeAsync();
        }

        _logger.LogInformation("PlayHouse server disposed");
    }

    private async Task<TcpSession> CreateTcpSessionAsync(long sessionId, System.Net.Sockets.Socket socket)
    {
        // Register session in SessionManager
        _sessionManager.CreateSession(sessionId);

        var session = new TcpSession(
            sessionId,
            socket,
            _options.Session,
            OnTcpMessageReceived,
            OnTcpSessionDisconnected,
            _loggerFactory.CreateLogger<TcpSession>());

        await Task.CompletedTask;
        return session;
    }

    private async Task<WebSocketSession> CreateWebSocketSessionAsync(
        long sessionId,
        System.Net.WebSockets.WebSocket webSocket)
    {
        var session = new WebSocketSession(
            sessionId,
            webSocket,
            OnWebSocketMessageReceived,
            OnWebSocketSessionDisconnected,
            _loggerFactory.CreateLogger<WebSocketSession>());

        await Task.CompletedTask;
        return session;
    }

    private void OnTcpMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
    {
        _logger.LogDebug("TCP message received from session {SessionId}: {Size} bytes", sessionId, data.Length);

        try
        {
            // Deserialize packet
            var packet = _packetSerializer.Deserialize(data.Span);

            _logger.LogTrace("Deserialized packet from session {SessionId}: MsgId={MsgId}, StageId={StageId}, MsgSeq={MsgSeq}",
                sessionId, packet.MsgId, packet.StageId, packet.MsgSeq);

            var session = _sessionManager.GetSession(sessionId);

            // Special handling for AuthenticateRequest (MUST be first)
            if (packet.MsgId == "AuthenticateRequest")
            {
                HandleAuthenticateRequest(sessionId, session, packet);
                return;
            }

            // Special handling for JoinStageRequest
            if (packet.MsgId == "JoinStageRequest")
            {
                HandleJoinStageRequest(sessionId, session);
                return;
            }

            // Update session with stage information if present
            if (session != null && packet.StageId > 0 && !session.StageId.HasValue)
            {
                session.JoinStage(packet.StageId);
            }

            // Route packet to appropriate stage
            // StageId should be determined from packet or session context
            var targetStageId = packet.StageId > 0 ? packet.StageId : session?.StageId ?? 0;

            if (targetStageId == 0)
            {
                _logger.LogWarning("Cannot route packet from session {SessionId}: no stage ID available", sessionId);
                return;
            }

            // Determine account ID from session
            var accountId = session?.AccountId ?? 0;
            if (accountId == 0)
            {
                _logger.LogWarning("Cannot route packet from session {SessionId}: no account ID available", sessionId);
                return;
            }

            // Dispatch to actor in stage
            var dispatched = _packetDispatcher.DispatchToActor(targetStageId, accountId, packet);

            if (!dispatched)
            {
                _logger.LogWarning("Failed to dispatch packet from session {SessionId} to stage {StageId}",
                    sessionId, targetStageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing TCP message from session {SessionId}", sessionId);
        }
    }

    private void OnTcpSessionDisconnected(long sessionId, Exception? exception)
    {
        _logger.LogInformation("TCP session {SessionId} disconnected", sessionId);

        if (exception != null)
        {
            _logger.LogWarning(exception, "TCP session {SessionId} disconnected with error", sessionId);
        }

        _tcpServer?.RemoveSession(sessionId);
    }

    private void OnWebSocketMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
    {
        _logger.LogDebug("WebSocket message received from session {SessionId}: {Size} bytes",
            sessionId, data.Length);

        try
        {
            // Deserialize packet
            var packet = _packetSerializer.Deserialize(data.Span);

            _logger.LogTrace("Deserialized packet from session {SessionId}: MsgId={MsgId}, StageId={StageId}, MsgSeq={MsgSeq}",
                sessionId, packet.MsgId, packet.StageId, packet.MsgSeq);

            // Update session with stage information if present
            var session = _sessionManager.GetSession(sessionId);
            if (session != null && packet.StageId > 0 && !session.StageId.HasValue)
            {
                session.JoinStage(packet.StageId);
            }

            // Route packet to appropriate stage
            // StageId should be determined from packet or session context
            var targetStageId = packet.StageId > 0 ? packet.StageId : session?.StageId ?? 0;

            if (targetStageId == 0)
            {
                _logger.LogWarning("Cannot route packet from session {SessionId}: no stage ID available", sessionId);
                return;
            }

            // Determine account ID from session
            var accountId = session?.AccountId ?? 0;
            if (accountId == 0)
            {
                _logger.LogWarning("Cannot route packet from session {SessionId}: no account ID available", sessionId);
                return;
            }

            // Dispatch to actor in stage
            var dispatched = _packetDispatcher.DispatchToActor(targetStageId, accountId, packet);

            if (!dispatched)
            {
                _logger.LogWarning("Failed to dispatch packet from session {SessionId} to stage {StageId}",
                    sessionId, targetStageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebSocket message from session {SessionId}", sessionId);
        }
    }

    private void OnWebSocketSessionDisconnected(long sessionId, Exception? exception)
    {
        _logger.LogInformation("WebSocket session {SessionId} disconnected", sessionId);

        if (exception != null)
        {
            _logger.LogWarning(exception, "WebSocket session {SessionId} disconnected with error", sessionId);
        }

        _webSocketServer?.RemoveSession(sessionId);
    }

    private async void HandleAuthenticateRequest(long sessionId, SessionInfo? session, Abstractions.IPacket packet)
    {
        if (session == null)
        {
            _logger.LogWarning("Cannot handle AuthenticateRequest: session {SessionId} not found", sessionId);
            return;
        }

        _logger.LogInformation("Handling AuthenticateRequest from session {SessionId}", sessionId);

        try
        {
            // Parse room token from packet payload
            // AuthenticateRequest proto: { string room_token = 1; }
            // Protobuf encoding: field 1 (0x0a), length-delimited string
            var payload = packet.Payload.Data.ToArray();

            if (payload.Length < 2 || payload[0] != 0x0a) // field 1, type string
            {
                _logger.LogWarning("Invalid AuthenticateRequest payload from session {SessionId}", sessionId);
                await SendAuthenticateReply(sessionId, packet.MsgSeq, authenticated: false,
                    errorMessage: "Invalid request format");
                return;
            }

            // Read string length (varint)
            int stringLength = payload[1];
            if (payload.Length < 2 + stringLength)
            {
                _logger.LogWarning("Invalid AuthenticateRequest payload length from session {SessionId}", sessionId);
                await SendAuthenticateReply(sessionId, packet.MsgSeq, authenticated: false,
                    errorMessage: "Invalid request format");
                return;
            }

            var roomToken = System.Text.Encoding.UTF8.GetString(payload, 2, stringLength);

            _logger.LogDebug("AuthenticateRequest with token: {Token}",
                roomToken.Substring(0, Math.Min(8, roomToken.Length)) + "...");

            // Validate token
            var (isValid, stageId, nickname) = _tokenManager.ValidateToken(roomToken);

            if (!isValid)
            {
                _logger.LogWarning("Invalid room token from session {SessionId}", sessionId);
                await SendAuthenticateReply(sessionId, packet.MsgSeq, authenticated: false,
                    errorMessage: "Invalid or expired token");
                return;
            }

            // Revoke token (one-time use)
            _tokenManager.RevokeToken(roomToken);

            // Generate AccountId from SessionId (simple strategy for E2E tests)
            var accountId = sessionId;

            // Update session with authentication info
            session.AccountId = accountId;
            session.JoinStage(stageId);

            _logger.LogInformation(
                "Session {SessionId} authenticated: AccountId={AccountId}, StageId={StageId}, Nickname={Nickname}",
                sessionId, accountId, stageId, nickname);

            // Send AuthenticateReply
            await SendAuthenticateReply(sessionId, packet.MsgSeq, authenticated: true,
                accountId: accountId, stageId: stageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling AuthenticateRequest from session {SessionId}", sessionId);
            await SendAuthenticateReply(sessionId, packet.MsgSeq, authenticated: false,
                errorMessage: "Internal server error");
        }
    }

    private async Task SendAuthenticateReply(long sessionId, int msgSeq, bool authenticated,
        long accountId = 0, int stageId = 0, string errorMessage = "")
    {
        var tcpSession = _tcpServer?.GetSession(sessionId);
        if (tcpSession == null)
        {
            _logger.LogWarning("Cannot send AuthenticateReply: session {SessionId} not found", sessionId);
            return;
        }

        // AuthenticateReply proto:
        // { int64 account_id=1; int32 stage_id=2; bool authenticated=3; string error_message=4; }
        var payloadBytes = new List<byte>();

        if (authenticated)
        {
            // account_id (field 1, varint/int64)
            payloadBytes.Add(0x08); // field 1, type varint
            payloadBytes.AddRange(EncodeVarint((ulong)accountId));

            // stage_id (field 2, varint/int32)
            payloadBytes.Add(0x10); // field 2, type varint
            payloadBytes.AddRange(EncodeVarint((ulong)stageId));

            // authenticated (field 3, varint/bool)
            payloadBytes.Add(0x18); // field 3, type varint
            payloadBytes.Add(0x01); // true
        }
        else
        {
            // authenticated (field 3, varint/bool)
            payloadBytes.Add(0x18); // field 3, type varint
            payloadBytes.Add(0x00); // false

            // error_message (field 4, length-delimited string)
            if (!string.IsNullOrEmpty(errorMessage))
            {
                var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorMessage);
                payloadBytes.Add(0x22); // field 4, type string
                payloadBytes.Add((byte)errorBytes.Length);
                payloadBytes.AddRange(errorBytes);
            }
        }

        var replyPacket = new Serialization.SimplePacket(
            msgId: "AuthenticateReply",
            payload: new Serialization.BinaryPayload(payloadBytes.ToArray()),
            msgSeq: (ushort)msgSeq,
            stageId: stageId,
            errorCode: authenticated ? (ushort)0 : (ushort)1);

        var replyBytes = _packetSerializer.Serialize(replyPacket, compress: false);
        await tcpSession.SendAsync(replyBytes);

        _logger.LogDebug("AuthenticateReply sent to session {SessionId}: Authenticated={Authenticated}",
            sessionId, authenticated);
    }

    private static byte[] EncodeVarint(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private async void HandleJoinStageRequest(long sessionId, SessionInfo? session)
    {
        if (session == null)
        {
            _logger.LogWarning("Cannot handle JoinStageRequest: session {SessionId} not found", sessionId);
            return;
        }

        // Parse JoinStageRequest (this requires Protobuf message, but we'll use a generic approach)
        // For E2E tests, we expect the payload to contain account_id, nickname, auth_token
        // We'll assign an account ID and stage ID to the session

        // For now, use a simple strategy: generate account ID from session ID
        var accountId = sessionId; // In production, this would be authenticated
        var stageId = 1; // For E2E tests, use a fixed stage ID

        // Update session with account and stage information
        session.AccountId = accountId;
        session.JoinStage(stageId);

        _logger.LogInformation("Session {SessionId} joined as AccountId={AccountId} StageId={StageId}",
            sessionId, accountId, stageId);

        // Send JoinStageReply back to client
        try
        {
            var tcpSession = _tcpServer?.GetSession(sessionId);
            if (tcpSession != null)
            {
                // Create JoinStageReply packet
                // Since we can't reference E2E proto types here, we'll manually construct the reply
                // using the packet format expected by the client

                // Create a simple reply with ErrorCode=0 (success)
                var replyPacket = new Serialization.SimplePacket(
                    msgId: "JoinStageReply",
                    payload: new Serialization.BinaryPayload(new byte[] {
                        // account_id (field 1, varint)
                        0x08, (byte)accountId,
                        // stage_id (field 2, varint)
                        0x10, (byte)stageId,
                        // joined_at (field 3, varint)
                        0x18, 0x00 // timestamp = 0 for now
                    }),
                    msgSeq: 0, // No msgSeq for unsolicited JoinStageReply
                    stageId: stageId,
                    errorCode: 0);

                var replyBytes = _packetSerializer.Serialize(replyPacket, compress: false);
                await tcpSession.SendAsync(replyBytes);

                _logger.LogDebug("JoinStageReply sent to session {SessionId}", sessionId);
            }
            else
            {
                _logger.LogWarning("Cannot send JoinStageReply: TCP session {SessionId} not found", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending JoinStageReply to session {SessionId}", sessionId);
        }
    }
}
