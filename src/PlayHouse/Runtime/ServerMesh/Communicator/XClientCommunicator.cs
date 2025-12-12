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
    private readonly HashSet<string> _connected = new();

    /// <inheritdoc/>
    public string Nid => _socket.Nid;

    /// <summary>
    /// Initializes a new instance of the <see cref="XClientCommunicator"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket.</param>
    public XClientCommunicator(IPlaySocket socket)
    {
        _socket = socket;
    }

    /// <inheritdoc/>
    public void Send(string targetNid, RuntimeRoutePacket packet)
    {
        _queue.Add(() =>
        {
            try
            {
                // Send using new IPlaySocket.Send(nid, packet) signature
                // Packet will be disposed inside IPlaySocket.Send
                _socket.Send(targetNid, packet);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[XClientCommunicator] Send error to {targetNid}: {ex.Message}");
            }
        });
    }

    /// <inheritdoc/>
    public void Connect(string targetNid, string address)
    {
        if (!_connected.Add(address))
        {
            return;
        }

        _queue.Add(() =>
        {
            try
            {
                _socket.Connect(address);
                Console.WriteLine($"[XClientCommunicator] Connected - nid:{targetNid}, endpoint:{address}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[XClientCommunicator] Connect error - nid:{targetNid}, endpoint:{address}, error:{ex.Message}");
            }
        });
    }

    /// <inheritdoc/>
    public void Disconnect(string targetNid, string address)
    {
        if (!_connected.Contains(address))
        {
            return;
        }

        try
        {
            _socket.Disconnect(address);
            _connected.Remove(address);
            Console.WriteLine($"[XClientCommunicator] Disconnected - nid:{targetNid}, endpoint:{address}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[XClientCommunicator] Disconnect error - nid:{targetNid}, endpoint:{address}, error:{ex.Message}");
        }
    }

    /// <summary>
    /// Processes queued actions. Called by MessageLoop thread.
    /// </summary>
    public void Communicate()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[XClientCommunicator] Error during communication - {ex.Message}");
            }
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
