namespace PlayHouse.Connector.Network;

internal static class PacketConst
{
    public const int MsgIdLimit = 256;
    public const int MaxBodySize = 1024 * 1024 * 2;
    public const int MinHeaderSize = 21; // ServiceId 없음 (Kairos는 23)
    public const string HeartBeat = "@Heart@Beat@";
    public const string Debug = "@Debug@";
    public const string Timeout = "@Timeout@";
}
