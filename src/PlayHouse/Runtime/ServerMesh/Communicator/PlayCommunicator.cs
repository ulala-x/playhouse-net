#nullable enable

using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Facade for bidirectional server-to-server communication.
/// Internally uses XClientCommunicator + XServerCommunicator + MessageLoop.
/// </summary>
internal sealed class PlayCommunicator : ICommunicator, ICommunicateListener
{
    private readonly IPlaySocket _socket;
    private readonly XClientCommunicator _client;
    private readonly XServerCommunicator _server;
    private readonly MessageLoop _messageLoop;
    private Action<string, RoutePacket>? _handler;
    private bool _disposed;

    /// <inheritdoc/>
    public string ServerId => _socket.ServerId;

    /// <inheritdoc/>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayCommunicator"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket.</param>
    public PlayCommunicator(IPlaySocket socket)
    {
        _socket = socket;
        _server = new XServerCommunicator(socket);
        _client = new XClientCommunicator(socket);
        _messageLoop = new MessageLoop(_server, _client);
    }

    /// <summary>
    /// Creates a PlayCommunicator from ServerConfig.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <returns>A new PlayCommunicator instance.</returns>
    public static PlayCommunicator Create(ServerConfig config)
    {
        var socket = ZmqPlaySocket.Create(config);
        return new PlayCommunicator(socket);
    }

    /// <inheritdoc/>
    public void OnReceive(RoutePacket packet)
    {
        _handler?.Invoke(packet.From, packet);
    }

    /// <inheritdoc/>
    public void Bind(string address)
    {
        // Socket binding is handled internally by XServerCommunicator
        // Address configuration should be done via ServerConfig/socket constructor
    }

    /// <inheritdoc/>
    public void Bind(ICommunicateListener listener)
    {
        _handler = (senderServerId, packet) => listener.OnReceive(packet);
    }

    /// <summary>
    /// Legacy method for backward compatibility.
    /// </summary>
    [Obsolete("Use Bind(ICommunicateListener) instead")]
    public void OnReceive(Action<string, RoutePacket> handler)
    {
        _handler = handler;
    }

    /// <inheritdoc/>
    public void Send(string targetServerId, RoutePacket packet) => _client.Send(targetServerId, packet);

    /// <inheritdoc/>
    public void Connect(string targetServerId, string address) => _client.Connect(targetServerId, address);

    /// <inheritdoc/>
    public void Disconnect(string targetServerId, string endpoint) => _client.Disconnect(targetServerId, endpoint);

    /// <inheritdoc/>
    public void Communicate()
    {
        // MessageLoop manages threads internally
        // This method is a no-op for ICommunicator interface compatibility
    }

    /// <inheritdoc/>
    public void Start()
    {
        if (IsRunning) return;

        // Bind the underlying socket first
        _socket.Bind();

        _server.Bind(this);
        _messageLoop.Start();
        IsRunning = true;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _messageLoop.Stop();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();                           // 1. _running = false
        _socket.TerminateContext();       // 2. 블로킹 해제
        _messageLoop.AwaitTermination();  // 3. 스레드 종료 대기
        _socket.Dispose();                // 4. 소켓 정리
    }
}
