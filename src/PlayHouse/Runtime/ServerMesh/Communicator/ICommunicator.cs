#nullable enable

using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Interface for sending messages to other servers.
/// </summary>
public interface IClientCommunicator
{
    /// <summary>
    /// Sends a packet to the specified target.
    /// </summary>
    /// <param name="targetServerId">Target server ID.</param>
    /// <param name="packet">Packet to send.</param>
    void Send(string targetServerId, RoutePacket packet);

    /// <summary>
    /// Connects to a remote server.
    /// </summary>
    /// <param name="targetServerId">Target server ID.</param>
    /// <param name="address">Connection address.</param>
    void Connect(string targetServerId, string address);

    /// <summary>
    /// Communicates with remote servers. Called from external thread.
    /// </summary>
    void Communicate();

    /// <summary>
    /// Disconnects from a remote server.
    /// </summary>
    /// <param name="targetServerId">Target server ID.</param>
    /// <param name="endpoint">Connection endpoint.</param>
    void Disconnect(string targetServerId, string endpoint);

    /// <summary>
    /// Stops the communicator.
    /// </summary>
    void Stop();
}

/// <summary>
/// Interface for receiving messages from other servers.
/// </summary>
public interface IServerCommunicator
{
    /// <summary>
    /// Binds a listener to receive incoming packets.
    /// </summary>
    /// <param name="listener">Listener to handle incoming packets.</param>
    void Bind(ICommunicateListener listener);

    /// <summary>
    /// Communicates with remote servers. Called from external thread.
    /// </summary>
    void Communicate();

    /// <summary>
    /// Stops the communicator.
    /// </summary>
    void Stop();
}

/// <summary>
/// Combined interface for bidirectional communication.
/// Provides both client and server communication capabilities.
/// </summary>
public interface ICommunicator : IClientCommunicator, IServerCommunicator, IDisposable
{
    /// <summary>
    /// Gets the ServerId of this communicator.
    /// </summary>
    string ServerId { get; }

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
