#nullable enable

using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Listener interface for receiving messages from the server communicator.
/// </summary>
public interface ICommunicateListener
{
    /// <summary>
    /// Called when a packet is received from a remote server.
    /// </summary>
    /// <param name="routePacket">The received route packet.</param>
    void OnReceive(RoutePacket routePacket);
}
