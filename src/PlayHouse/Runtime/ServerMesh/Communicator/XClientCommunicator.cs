#nullable enable

using System.Collections.Concurrent;
using System.Threading.Channels;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Optimized client communicator using System.Threading.Channels to avoid delegate allocations.
/// Maintains ZMQ thread-safety by ensuring all socket operations happen on a single dedicated thread.
/// </summary>
internal sealed class XClientCommunicator : IClientCommunicator
{
    private readonly IPlaySocket _socket;
    private readonly Channel<SendRequest> _sendChannel;
    private readonly ConcurrentDictionary<string, byte> _connected = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Internal struct to hold send data without heap allocation.
    /// </summary>
    private readonly struct SendRequest
    {
        public readonly string TargetServerId;
        public readonly RoutePacket Packet;

        public SendRequest(string targetServerId, RoutePacket packet)
        {
            TargetServerId = targetServerId;
            Packet = packet;
        }
    }

    public string ServerId => _socket.ServerId;

    public XClientCommunicator(IPlaySocket socket)
    {
        _socket = socket;
        // SingleReader optimization: Only the Communicate() loop reads from this channel.
        _sendChannel = Channel.CreateUnbounded<SendRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Send(string targetServerId, RoutePacket packet)
    {
        // Zero-allocation queueing: Just writing a struct to the channel.
        if (!_sendChannel.Writer.TryWrite(new SendRequest(targetServerId, packet)))
        {
            packet.Dispose();
        }
    }

    public void Connect(string targetServerId, string address)
    {
        if (!_connected.TryAdd(address, 0)) return;
        // Connect is usually called at startup, so direct call is safe if thread not yet started
        // or we can use a specialized command in the channel if dynamic connect is needed.
        _socket.Connect(address);
    }

    public void Disconnect(string targetServerId, string address)
    {
        if (!_connected.TryRemove(address, out _)) return;
        _socket.Disconnect(address);
    }

    /// <summary>
    /// Processes queued messages. Called by dedicated MessageLoop thread.
    /// </summary>
    public void Communicate()
    {
        var reader = _sendChannel.Reader;
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                // Wait for data without spinning
                if (reader.TryRead(out var request))
                {
                    _socket.Send(request.TargetServerId, request.Packet);

                    // Batching: Process all available messages before yielding
                    while (reader.TryRead(out request))
                    {
                        _socket.Send(request.TargetServerId, request.Packet);
                    }
                }
                else
                {
                    // Block asynchronously until data arrives
                    if (!reader.WaitToReadAsync(_cts.Token).AsTask().GetAwaiter().GetResult())
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[XClientCommunicator] Send loop error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        _sendChannel.Writer.TryComplete();
    }
}