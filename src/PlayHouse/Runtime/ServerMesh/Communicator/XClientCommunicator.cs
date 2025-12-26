#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Client-side communicator for sending messages to servers.
/// Thread-safe queue-based implementation managed by MessageLoop.
/// </summary>
internal sealed class XClientCommunicator : IClientCommunicator
{
    private readonly IPlaySocket _socket;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly ConcurrentDictionary<string, byte> _connected = new();

    /// <inheritdoc/>
    public string ServerId => _socket.ServerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XClientCommunicator"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket.</param>
    public XClientCommunicator(IPlaySocket socket)
    {
        _socket = socket;
    }

    /// <inheritdoc/>
    public void Send(string targetServerId, RoutePacket packet)
    {
        _queue.Add(() =>
        {
            // Send using new IPlaySocket.Send(serverId, packet) signature
            // Packet will be disposed inside IPlaySocket.Send
            _socket.Send(targetServerId, packet);
        });
    }

    /// <inheritdoc/>
    public void Connect(string targetServerId, string address)
    {
        if (!_connected.TryAdd(address, 0))
        {
            return;
        }

        _queue.Add(() =>
        {
            try
            {
                _socket.Connect(address);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[XClientCommunicator] Connect failed to {address}: {ex.Message}");
                _connected.TryRemove(address, out _);
                throw;
            }
        });
    }

    /// <inheritdoc/>
    public void Disconnect(string targetServerId, string address)
    {
        if (!_connected.ContainsKey(address))
        {
            return;
        }

        _queue.Add(() =>
        {
            _socket.Disconnect(address);
            _connected.TryRemove(address, out _);
        });
    }

    /// <summary>
    /// Processes queued actions. Called by MessageLoop thread.
    /// </summary>
    public void Communicate()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action.Invoke();
        }
    }

    /// <summary>
    /// Stops accepting new actions and signals completion.
    /// </summary>
    public void Stop()
    {
        _queue.CompleteAdding();
    }
}
