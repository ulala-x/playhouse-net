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
    private Action<string, RuntimeRoutePacket>? _handler;
    private bool _disposed;

    /// <inheritdoc/>
    public string Nid => _socket.Nid;

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
        var socket = NetMqPlaySocket.Create(config);
        return new PlayCommunicator(socket);
    }

    /// <inheritdoc/>
    public void OnReceive(RuntimeRoutePacket packet)
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
        _handler = (senderNid, packet) => listener.OnReceive(packet);
    }

    /// <summary>
    /// Legacy method for backward compatibility.
    /// </summary>
    [Obsolete("Use Bind(ICommunicateListener) instead")]
    public void OnReceive(Action<string, RuntimeRoutePacket> handler)
    {
        _handler = handler;
    }

    /// <inheritdoc/>
    public void Send(string targetNid, RuntimeRoutePacket packet) => _client.Send(targetNid, packet);

    /// <inheritdoc/>
    public void Connect(string targetNid, string address) => _client.Connect(targetNid, address);

    /// <inheritdoc/>
    public void Disconnect(string targetNid, string endpoint) => _client.Disconnect(targetNid, endpoint);

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

        Stop();
        _messageLoop.AwaitTermination();
        _socket.Dispose();
    }
}
