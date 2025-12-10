#nullable enable

using PlayHouse.Runtime.Message;

namespace PlayHouse.Runtime.Communicator;

/// <summary>
/// Interface for sending messages to other servers.
/// </summary>
public interface IClientCommunicator
{
    /// <summary>
    /// Gets the NID of this communicator.
    /// </summary>
    string Nid { get; }

    /// <summary>
    /// Sends a packet to the specified target.
    /// </summary>
    /// <param name="targetNid">Target node ID.</param>
    /// <param name="packet">Packet to send.</param>
    void Send(string targetNid, RuntimeRoutePacket packet);

    /// <summary>
    /// Connects to a remote server.
    /// </summary>
    /// <param name="targetNid">Target node ID.</param>
    /// <param name="address">Connection address.</param>
    void Connect(string targetNid, string address);

    /// <summary>
    /// Disconnects from a remote server.
    /// </summary>
    /// <param name="targetNid">Target node ID.</param>
    void Disconnect(string targetNid);
}

/// <summary>
/// Interface for receiving messages from other servers.
/// </summary>
public interface IServerCommunicator
{
    /// <summary>
    /// Gets the NID of this communicator.
    /// </summary>
    string Nid { get; }

    /// <summary>
    /// Registers a message handler for incoming packets.
    /// </summary>
    /// <param name="handler">Handler to process received packets.</param>
    void OnReceive(Action<string, RuntimeRoutePacket> handler);

    /// <summary>
    /// Starts the receive loop.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the receive loop.
    /// </summary>
    void Stop();
}

/// <summary>
/// Combined interface for bidirectional communication.
/// </summary>
public interface ICommunicator : IClientCommunicator, IServerCommunicator, IDisposable
{
    /// <summary>
    /// Gets whether the communicator is running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Binds to the specified address for receiving messages.
    /// </summary>
    /// <param name="address">Bind address.</param>
    void Bind(string address);
}
