using PlayHouse.Communicator.Message;
using PlayHouse.Communicator.PlaySocket;

namespace PlayHouse.Production.Shared;

public class PlayhouseOption
{
    public int MaxBufferPoolSize = 1024 * 1024 * 100;
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public ushort ServiceId { get; set; }
    public int ServerId { get; set; } // 1~ 4095
    public IServiceProvider? ServiceProvider { get; set; }
    public int RequestTimeoutMSec { get; set; } = 10000;
    public bool ShowQps { get; set; }
    public bool DebugMode { get; set; }
    public int ServerTimeLimitsMs { get; set; } = 30000;
    public PlaySocketConfig PlaySocketConfig { get; set; } = new();
    public Func<string, IPayload, ushort, IPacket>? PacketProducer { get; set; }
}