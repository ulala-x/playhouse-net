#nullable enable

using Net.Zmq;
using Net.Zmq.Core.Native;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Server-side communicator for receiving messages from clients/servers.
/// Thread management handled by MessageLoop.
/// </summary>
internal sealed class XServerCommunicator : IServerCommunicator
{
    private readonly IPlaySocket _socket;
    private ICommunicateListener? _listener;
    private volatile bool _running = true;

    /// <inheritdoc/>
    public string ServerId => _socket.ServerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XServerCommunicator"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket.</param>
    public XServerCommunicator(IPlaySocket socket)
    {
        _socket = socket;
    }

    /// <summary>
    /// Binds the socket and registers message listener.
    /// </summary>
    /// <param name="listener">Message listener to handle received packets.</param>
    public void Bind(ICommunicateListener listener)
    {
        _listener = listener;
        // Note: Bind address should be configured in socket constructor or separately
        // For now, assuming socket has default bind configuration
    }

    /// <summary>
    /// Processes incoming messages. Called by MessageLoop thread.
    /// </summary>
    public void Communicate()
    {
        while (_running)
        {
            try
            {
                // Receive with 10ms timeout - no additional sleep needed as timeout provides waiting
                var packet = _socket.Receive();
                if (packet != null)
                {
                    _listener?.OnReceive(packet);
                }
            }
            catch (ZmqException ex) when (ex.ErrorNumber == ZmqConstants.ETERM)
            {
                // Context terminated - 정상 종료
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[XServerCommunicator] Receive error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Stops the receive loop.
    /// </summary>
    public void Stop()
    {
        _running = false;
    }
}
