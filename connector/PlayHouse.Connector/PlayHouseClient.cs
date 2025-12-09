namespace PlayHouse.Connector;

using System.Collections.Concurrent;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Connector.Connection;
using PlayHouse.Connector.Events;
using PlayHouse.Connector.Protocol;

/// <summary>
/// Main PlayHouse client implementation for server communication.
/// </summary>
public sealed class PlayHouseClient : IPlayHouseClient
{
    private readonly PlayHouseClientOptions _options;
    private readonly IConnection _connection;
    private readonly RequestTracker _requestTracker;
    private readonly PacketEncoder _encoder;
    private readonly PacketDecoder _decoder;
    private readonly ILogger<PlayHouseClient>? _logger;
    private readonly ConcurrentDictionary<string, List<Delegate>> _handlers = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private ConnectionState _state = ConnectionState.Disconnected;
    private int _stageId;
    private long _accountId;
    private string? _lastEndpoint;
    private string? _lastToken;
    private int _reconnectAttempts;

    /// <inheritdoc/>
    public ConnectionState State => _state;

    /// <inheritdoc/>
    public int StageId => _stageId;

    /// <inheritdoc/>
    public long AccountId => _accountId;

    /// <inheritdoc/>
    public bool IsConnected => _state == ConnectionState.Connected;

    /// <inheritdoc/>
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <inheritdoc/>
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <inheritdoc/>
    public event EventHandler<ClientErrorEventArgs>? ErrorOccurred;

    /// <inheritdoc/>
    public event EventHandler<DisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayHouseClient"/> class.
    /// </summary>
    /// <param name="options">Client configuration options</param>
    /// <param name="logger">Optional logger</param>
    public PlayHouseClient(PlayHouseClientOptions? options = null, ILogger<PlayHouseClient>? logger = null)
    {
        _options = options ?? new PlayHouseClientOptions();
        _options.Validate();
        _logger = logger;

        _encoder = new PacketEncoder();
        _decoder = new PacketDecoder(null);
        _requestTracker = new RequestTracker(null);

        // Connection will be created based on endpoint scheme
        _connection = null!; // Set during ConnectAsync
    }

    /// <summary>
    /// Initializes a new instance with a specific connection (for testing).
    /// </summary>
    internal PlayHouseClient(
        IConnection connection,
        PlayHouseClientOptions? options = null,
        ILogger<PlayHouseClient>? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new PlayHouseClientOptions();
        _options.Validate();
        _logger = logger;

        _encoder = new PacketEncoder();
        _decoder = new PacketDecoder(null);
        _requestTracker = new RequestTracker(null);

        SetupConnectionHandlers();
    }

    /// <inheritdoc/>
    public async Task<JoinRoomResult> ConnectAsync(
        string endpoint,
        string roomToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(endpoint))
        {
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        }

        if (string.IsNullOrEmpty(roomToken))
        {
            throw new ArgumentException("Room token cannot be null or empty.", nameof(roomToken));
        }

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException($"Cannot connect in state: {_state}");
            }

            UpdateState(ConnectionState.Connecting);

            _lastEndpoint = endpoint;
            _lastToken = roomToken;

            // Parse endpoint
            var uri = new Uri(endpoint);
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : (uri.Scheme == "wss" || uri.Scheme == "ws" ? 80 : 8080);

            // Create appropriate connection
            IConnection connection;
            if (uri.Scheme == "ws" || uri.Scheme == "wss")
            {
                connection = new WebSocketConnection(_options, null);
            }
            else if (uri.Scheme == "tcp")
            {
                connection = new TcpConnection(_options, null);
            }
            else
            {
                throw new ArgumentException($"Unsupported scheme: {uri.Scheme}. Use 'tcp://', 'ws://', or 'wss://'.");
            }

            // Replace connection if needed (constructor may have set null!)
            if (_connection == null)
            {
                typeof(PlayHouseClient)
                    .GetField(nameof(_connection), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(this, connection);
                SetupConnectionHandlers();
            }

            // Connect
            await _connection.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

            // TODO: Send JoinRoomRequest with roomToken
            // For now, simulate successful join
            _stageId = 1; // Will be set from server response
            _accountId = 12345; // Will be set from server response

            UpdateState(ConnectionState.Connected);
            _reconnectAttempts = 0;

            _logger?.LogInformation("Connected to {Endpoint} with StageId={StageId}", endpoint, _stageId);

            return new JoinRoomResult(true, 0, _stageId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to {Endpoint}", endpoint);
            UpdateState(ConnectionState.Disconnected);
            RaiseError(ex, "Connection", isRecoverable: true);
            return new JoinRoomResult(false, 1, 0, ex.Message);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(string? reason = null)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state == ConnectionState.Disconnected || _state == ConnectionState.Disconnecting)
            {
                return;
            }

            UpdateState(ConnectionState.Disconnecting);

            _logger?.LogInformation("Disconnecting: {Reason}", reason ?? "User requested");

            // Cancel all pending requests
            _requestTracker.CancelAll();

            // Disconnect connection
            if (_connection != null)
            {
                await _connection.DisconnectAsync().ConfigureAwait(false);
            }

            UpdateState(ConnectionState.Disconnected);

            RaiseDisconnected(reason, wasIntentional: true);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_lastEndpoint) || string.IsNullOrEmpty(_lastToken))
        {
            _logger?.LogWarning("Cannot reconnect: No previous connection details");
            return false;
        }

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException($"Cannot reconnect in state: {_state}");
            }

            UpdateState(ConnectionState.Reconnecting);

            _reconnectAttempts++;
            _logger?.LogInformation("Reconnecting (attempt {Attempt}/{Max})", _reconnectAttempts, _options.MaxReconnectAttempts);

            var result = await ConnectAsync(_lastEndpoint, _lastToken, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                _reconnectAttempts = 0;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Reconnection failed");
            UpdateState(ConnectionState.Disconnected);
            return false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<Response<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IMessage, new()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected.");
        }

        var effectiveTimeout = timeout ?? _options.RequestTimeout;
        var msgSeq = _requestTracker.GetNextMsgSeq();
        var msgId = GetMessageId<TRequest>();

        _logger?.LogDebug("Sending request: Type={Type}, MsgSeq={MsgSeq}, MsgId={MsgId}",
            typeof(TRequest).Name, msgSeq, msgId);

        // Track request
        var responseTask = _requestTracker.TrackRequestAsync<TResponse>(msgSeq, effectiveTimeout, cancellationToken);

        // Encode and send
        try
        {
            var packet = _encoder.EncodeRequest(request, msgSeq, msgId);
            await _connection.SendAsync(packet, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _requestTracker.FailRequest(msgSeq, ex);
            throw;
        }

        // Wait for response
        return await responseTask.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync<T>(T message) where T : IMessage
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected.");
        }

        var msgId = GetMessageId<T>();

        _logger?.LogDebug("Sending message: Type={Type}, MsgId={MsgId}", typeof(T).Name, msgId);

        var packet = _encoder.EncodeMessage(message, msgId);
        await _connection.SendAsync(packet).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<LeaveRoomResult> LeaveRoomAsync(string? reason = null)
    {
        if (!IsConnected)
        {
            return new LeaveRoomResult(false, 1, "Not connected");
        }

        _logger?.LogInformation("Leaving room: {Reason}", reason ?? "User requested");

        // TODO: Send LeaveRoomRequest to server

        await DisconnectAsync(reason).ConfigureAwait(false);

        return new LeaveRoomResult(true, 0);
    }

    /// <inheritdoc/>
    public IDisposable On<T>(Action<T> handler) where T : IMessage, new()
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var messageType = typeof(T).FullName!;
        var handlers = _handlers.GetOrAdd(messageType, _ => new List<Delegate>());

        lock (handlers)
        {
            handlers.Add(handler);
        }

        _logger?.LogDebug("Registered handler for message type: {Type}", typeof(T).Name);

        return new HandlerUnsubscriber(() =>
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        });
    }

    /// <inheritdoc/>
    public IDisposable On<T>(Func<T, Task> handler) where T : IMessage, new()
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var messageType = typeof(T).FullName!;
        var handlers = _handlers.GetOrAdd(messageType, _ => new List<Delegate>());

        lock (handlers)
        {
            handlers.Add(handler);
        }

        _logger?.LogDebug("Registered async handler for message type: {Type}", typeof(T).Name);

        return new HandlerUnsubscriber(() =>
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        });
    }

    private void SetupConnectionHandlers()
    {
        _connection.DataReceived += OnDataReceived;
        _connection.Disconnected += OnConnectionDisconnected;
    }

    private void OnDataReceived(object? sender, byte[] data)
    {
        try
        {
            var packets = _decoder.ProcessData(data);

            foreach (var packet in packets)
            {
                ProcessPacket(packet);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing received data");
            RaiseError(ex, "Protocol", isRecoverable: false);
        }
    }

    private void ProcessPacket(Packet.ServerPacket packet)
    {
        _logger?.LogTrace("Processing packet: MsgSeq={MsgSeq}, MsgId={MsgId}, ErrorCode={ErrorCode}",
            packet.MsgSeq, packet.MsgId, packet.ErrorCode);

        // Check if this is a response to a pending request
        if (packet.MsgSeq > 0)
        {
            _requestTracker.CompleteRequest(packet.MsgSeq, packet.ErrorCode, packet.Payload.Memory);
            return;
        }

        // This is a push message from server
        try
        {
            var messageType = GetMessageType(packet.MsgId);
            if (messageType == null)
            {
                _logger?.LogWarning("Unknown message ID: {MsgId}", packet.MsgId);
                return;
            }

            var message = _decoder.DecodeMessage(messageType, packet.Payload.Memory);

            // Raise MessageReceived event
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(packet.MsgId, packet.MsgSeq, message));

            // Invoke registered handlers
            InvokeHandlers(messageType, message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing push message: MsgId={MsgId}", packet.MsgId);
            RaiseError(ex, "MessageHandling", isRecoverable: true);
        }
    }

    private void InvokeHandlers(Type messageType, IMessage message)
    {
        var typeName = messageType.FullName!;

        if (!_handlers.TryGetValue(typeName, out var handlers))
        {
            return;
        }

        List<Delegate> handlersCopy;
        lock (handlers)
        {
            handlersCopy = new List<Delegate>(handlers);
        }

        foreach (var handler in handlersCopy)
        {
            try
            {
                if (handler is Action<IMessage> syncHandler)
                {
                    syncHandler(message);
                }
                else if (handler is Func<IMessage, Task> asyncHandler)
                {
                    _ = asyncHandler(message); // Fire and forget
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in message handler for {Type}", messageType.Name);
                RaiseError(ex, "MessageHandler", isRecoverable: true);
            }
        }
    }

    private void OnConnectionDisconnected(object? sender, Exception? exception)
    {
        _logger?.LogWarning(exception, "Connection lost");

        UpdateState(ConnectionState.Disconnected);

        // Cancel all pending requests
        _requestTracker.CancelAll();

        var shouldReconnect = _options.AutoReconnect && _reconnectAttempts < _options.MaxReconnectAttempts;

        RaiseDisconnected(
            exception?.Message,
            exception,
            wasIntentional: false,
            shouldReconnect);

        if (shouldReconnect)
        {
            _ = Task.Run(async () =>
            {
                var delay = CalculateReconnectDelay();
                _logger?.LogInformation("Auto-reconnecting in {Delay}ms", delay.TotalMilliseconds);

                await Task.Delay(delay).ConfigureAwait(false);
                await ReconnectAsync().ConfigureAwait(false);
            });
        }
    }

    private TimeSpan CalculateReconnectDelay()
    {
        var delay = _options.ReconnectDelay;

        for (int i = 1; i < _reconnectAttempts; i++)
        {
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _options.ReconnectBackoffMultiplier);
        }

        return delay > _options.MaxReconnectDelay ? _options.MaxReconnectDelay : delay;
    }

    private void UpdateState(ConnectionState newState)
    {
        if (_state == newState)
        {
            return;
        }

        var oldState = _state;
        _state = newState;

        _logger?.LogDebug("State changed: {OldState} → {NewState}", oldState, newState);

        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState));
    }

    private void RaiseError(Exception error, string context, bool isRecoverable)
    {
        ErrorOccurred?.Invoke(this, new ClientErrorEventArgs(error, context, isRecoverable));
    }

    private void RaiseDisconnected(
        string? reason = null,
        Exception? exception = null,
        bool wasIntentional = false,
        bool shouldReconnect = false)
    {
        Disconnected?.Invoke(this, new DisconnectedEventArgs(reason, exception, wasIntentional, shouldReconnect));
    }

    private static ushort GetMessageId<T>() where T : IMessage
    {
        // TODO: Implement message ID lookup from registry
        // For now, use simple hash of type name
        var typeName = typeof(T).FullName ?? typeof(T).Name;
        return (ushort)Math.Abs(typeName.GetHashCode() % ushort.MaxValue);
    }

    private static Type? GetMessageType(ushort msgId)
    {
        // TODO: Implement reverse lookup from message ID to type
        return null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync("Client disposing").ConfigureAwait(false);

        _requestTracker.Dispose();
        _stateLock.Dispose();

        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class HandlerUnsubscriber : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public HandlerUnsubscriber(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _unsubscribe();
                _disposed = true;
            }
        }
    }
}
