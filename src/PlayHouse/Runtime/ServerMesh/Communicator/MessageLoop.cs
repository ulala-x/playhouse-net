#nullable enable

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Manages message communication threads for server and client communicators.
/// </summary>
internal class MessageLoop
{
    private readonly IClientCommunicator _client;
    private readonly Thread _clientThread;
    private readonly IServerCommunicator _server;
    private readonly Thread _serverThread;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageLoop"/> class.
    /// </summary>
    /// <param name="server">Server communicator instance.</param>
    /// <param name="client">Client communicator instance.</param>
    public MessageLoop(IServerCommunicator server, IClientCommunicator client)
    {
        _server = server;
        _client = client;

        _serverThread = new Thread(() =>
        {
            Console.WriteLine("start Server Communicator");
            _server.Communicate();
        })
        {
            Name = "server:Communicator"
        };

        _clientThread = new Thread(() =>
        {
            Console.WriteLine("start client Communicator");
            _client.Communicate();
        })
        {
            Name = "client:Communicator"
        };
    }

    /// <summary>
    /// Starts both server and client communication threads.
    /// </summary>
    public void Start()
    {
        _serverThread.Start();
        _clientThread.Start();
    }

    /// <summary>
    /// Stops both server and client communicators.
    /// </summary>
    public void Stop()
    {
        _server.Stop();
        _client.Stop();
    }

    /// <summary>
    /// Waits for both communication threads to terminate.
    /// </summary>
    public void AwaitTermination()
    {
        _clientThread.Join();
        _serverThread.Join();
    }
}
