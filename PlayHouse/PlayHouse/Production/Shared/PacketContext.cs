namespace PlayHouse.Production.Shared;

internal class AsyncCore : IAsyncCore
{
    private readonly AsyncLocal<List<SendPacketInfo>?> _sendPackets = new();

    public void Init()
    {
        _sendPackets.Value = new List<SendPacketInfo>();
    }


    public List<SendPacketInfo> GetSendPackets()
    {
        return _sendPackets.Value ?? new List<SendPacketInfo>();
    }

    public void Add(SendTarget target, ushort msgSeq, IPacket? packet)
    {
        _sendPackets.Value?.Add(new SendPacketInfo { Target = target, Packet = packet, MsgSeq = msgSeq });
    }

    public void Add(SendTarget target, ushort msgSeq, ushort errorCode)
    {
        _sendPackets.Value?.Add(new SendPacketInfo
            { Target = target, Packet = null, ErrorCode = errorCode, MsgSeq = msgSeq });
    }

    public void Clear()
    {
        _sendPackets.Value = null;
    }
}

public class PacketContext
{
    private IAsyncCore _core = new AsyncCore();

    internal static IAsyncCore AsyncCore
    {
        get => Instance._core;
        set => Instance._core = value;
    }

    public static PacketContext Instance { get; } = new();
}