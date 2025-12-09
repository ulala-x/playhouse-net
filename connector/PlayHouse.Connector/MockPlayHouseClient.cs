namespace PlayHouse.Connector;

using System.Collections.Concurrent;
using Google.Protobuf;
using PlayHouse.Connector.Events;

/// <summary>
/// Mock implementation of IPlayHouseClient for unit testing without a real server.
/// </summary>
public sealed class MockPlayHouseClient : IPlayHouseClient
{
    private readonly ConcurrentQueue<object> _sentMessages = new();
    private readonly ConcurrentQueue<object> _receivedMessages = new();
    private readonly ConcurrentDictionary<string, List<Delegate>> _handlers = new();
    private readonly object _stateLock = new();

    private ConnectionState _state = ConnectionState.Disconnected;
    private int _stageId;
    private long _accountId;
    private string? _lastEndpoint;
    private string? _lastToken;

    /// <summary>
    /// Gets all messages that were sent through SendAsync or RequestAsync.
    /// </summary>
    public IReadOnlyCollection<object> SentMessages => _sentMessages.ToArray();

    /// <summary>
    /// Gets all messages that were simulated via SimulateMessage.
    /// </summary>
    public IReadOnlyCollection<object> ReceivedMessages => _receivedMessages.ToArray();

    /// <summary>
    /// Gets or sets the simulated request delay (default: 10ms).
    /// </summary>
    public TimeSpan SimulatedRequestDelay { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Gets or sets the default response for RequestAsync calls.
    /// Can be overridden per request type using SetRequestResponse.
    /// </summary>
    public Func<object, object>? DefaultResponseFactory { get; set; }

    private readonly Dictionary<Type, Func<object, object>> _responseFactories = new();
    private readonly Dictionary<Type, ushort> _errorCodes = new();

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
    /// Initializes a new instance of the <see cref="MockPlayHouseClient"/> class.
    /// </summary>
    public MockPlayHouseClient()
    {
    }

    /// <inheritdoc/>
    public Task<JoinRoomResult> ConnectAsync(
        string endpoint,
        string roomToken,
        CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException($"Cannot connect in state: {_state}");
            }

            UpdateState(ConnectionState.Connecting);

            _lastEndpoint = endpoint;
            _lastToken = roomToken;
            _stageId = 1;
            _accountId = 99999; // Mock account ID

            UpdateState(ConnectionState.Connected);

            return Task.FromResult(new JoinRoomResult(true, 0, _stageId));
        }
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(string? reason = null)
    {
        lock (_stateLock)
        {
            if (_state == ConnectionState.Disconnected || _state == ConnectionState.Disconnecting)
            {
                return Task.CompletedTask;
            }

            UpdateState(ConnectionState.Disconnecting);
            UpdateState(ConnectionState.Disconnected);

            Disconnected?.Invoke(this, new DisconnectedEventArgs(reason, wasIntentional: true));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_lastEndpoint) || string.IsNullOrEmpty(_lastToken))
        {
            return Task.FromResult(false);
        }

        lock (_stateLock)
        {
            if (_state != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException($"Cannot reconnect in state: {_state}");
            }

            UpdateState(ConnectionState.Reconnecting);
            UpdateState(ConnectionState.Connected);
        }

        return Task.FromResult(true);
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

        _sentMessages.Enqueue(request);

        // Simulate network delay
        await Task.Delay(SimulatedRequestDelay, cancellationToken).ConfigureAwait(false);

        // Check for error code override
        if (_errorCodes.TryGetValue(typeof(TRequest), out var errorCode) && errorCode != 0)
        {
            return new Response<TResponse>(false, errorCode, default, $"Simulated error: {errorCode}");
        }

        // Generate response
        TResponse? response = default;

        if (_responseFactories.TryGetValue(typeof(TRequest), out var factory))
        {
            response = (TResponse)factory(request);
        }
        else if (DefaultResponseFactory != null)
        {
            response = (TResponse)DefaultResponseFactory(request);
        }
        else
        {
            response = new TResponse();
        }

        return new Response<TResponse>(true, 0, response);
    }

    /// <inheritdoc/>
    public ValueTask SendAsync<T>(T message) where T : IMessage
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected.");
        }

        _sentMessages.Enqueue(message);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<LeaveRoomResult> LeaveRoomAsync(string? reason = null)
    {
        if (!IsConnected)
        {
            return Task.FromResult(new LeaveRoomResult(false, 1, "Not connected"));
        }

        lock (_stateLock)
        {
            UpdateState(ConnectionState.Disconnecting);
            UpdateState(ConnectionState.Disconnected);
        }

        Disconnected?.Invoke(this, new DisconnectedEventArgs(reason, wasIntentional: true));

        return Task.FromResult(new LeaveRoomResult(true, 0));
    }

    /// <inheritdoc/>
    public IDisposable On<T>(Action<T> handler) where T : IMessage, new()
    {
        var typeName = typeof(T).FullName!;
        var handlers = _handlers.GetOrAdd(typeName, _ => new List<Delegate>());

        lock (handlers)
        {
            handlers.Add(handler);
        }

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
        var typeName = typeof(T).FullName!;
        var handlers = _handlers.GetOrAdd(typeName, _ => new List<Delegate>());

        lock (handlers)
        {
            handlers.Add(handler);
        }

        return new HandlerUnsubscriber(() =>
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        });
    }

    // Mock-specific methods

    /// <summary>
    /// Simulates receiving a message from the server.
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to simulate</param>
    public void SimulateMessage<T>(T message) where T : IMessage
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Cannot simulate message when not connected.");
        }

        _receivedMessages.Enqueue(message);

        // Raise MessageReceived event
        MessageReceived?.Invoke(this, new MessageReceivedEventArgs(1, 0, message));

        // Invoke registered handlers
        var typeName = typeof(T).FullName!;
        if (_handlers.TryGetValue(typeName, out var handlers))
        {
            List<Delegate> handlersCopy;
            lock (handlers)
            {
                handlersCopy = new List<Delegate>(handlers);
            }

            foreach (var handler in handlersCopy)
            {
                try
                {
                    if (handler is Action<T> syncHandler)
                    {
                        syncHandler(message);
                    }
                    else if (handler is Func<T, Task> asyncHandler)
                    {
                        _ = asyncHandler(message); // Fire and forget
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, new ClientErrorEventArgs(ex, "MessageHandler", isRecoverable: true));
                }
            }
        }
    }

    /// <summary>
    /// Simulates a disconnection from the server.
    /// </summary>
    /// <param name="reason">Disconnection reason</param>
    /// <param name="exception">Optional exception that caused disconnection</param>
    public void SimulateDisconnect(string? reason = null, Exception? exception = null)
    {
        lock (_stateLock)
        {
            if (_state == ConnectionState.Disconnected)
            {
                return;
            }

            UpdateState(ConnectionState.Disconnected);
        }

        Disconnected?.Invoke(this, new DisconnectedEventArgs(reason, exception, wasIntentional: false, shouldReconnect: false));
    }

    /// <summary>
    /// Simulates an error occurring in the client.
    /// </summary>
    /// <param name="error">Error to simulate</param>
    /// <param name="context">Error context</param>
    /// <param name="isRecoverable">Whether the error is recoverable</param>
    public void SimulateError(Exception error, string context = "Simulated", bool isRecoverable = true)
    {
        ErrorOccurred?.Invoke(this, new ClientErrorEventArgs(error, context, isRecoverable));
    }

    /// <summary>
    /// Sets a custom response factory for a specific request type.
    /// </summary>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TResponse">Response type</typeparam>
    /// <param name="factory">Factory function to create response from request</param>
    public void SetRequestResponse<TRequest, TResponse>(Func<TRequest, TResponse> factory)
        where TRequest : IMessage
        where TResponse : IMessage
    {
        _responseFactories[typeof(TRequest)] = req => factory((TRequest)req)!;
    }

    /// <summary>
    /// Sets an error code to return for a specific request type.
    /// </summary>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <param name="errorCode">Error code to return (0 = success)</param>
    public void SetRequestError<TRequest>(ushort errorCode) where TRequest : IMessage
    {
        _errorCodes[typeof(TRequest)] = errorCode;
    }

    /// <summary>
    /// Clears all sent and received message queues.
    /// </summary>
    public void ClearMessages()
    {
        _sentMessages.Clear();
        _receivedMessages.Clear();
    }

    /// <summary>
    /// Clears all registered response factories and error codes.
    /// </summary>
    public void ClearResponseMocks()
    {
        _responseFactories.Clear();
        _errorCodes.Clear();
    }

    /// <summary>
    /// Resets the mock client to initial state.
    /// </summary>
    public void Reset()
    {
        lock (_stateLock)
        {
            _state = ConnectionState.Disconnected;
            _stageId = 0;
            _accountId = 0;
            _lastEndpoint = null;
            _lastToken = null;
        }

        ClearMessages();
        ClearResponseMocks();
        _handlers.Clear();
    }

    private void UpdateState(ConnectionState newState)
    {
        if (_state == newState)
        {
            return;
        }

        var oldState = _state;
        _state = newState;

        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_state != ConnectionState.Disconnected)
            {
                UpdateState(ConnectionState.Disconnected);
            }
        }

        return ValueTask.CompletedTask;
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
