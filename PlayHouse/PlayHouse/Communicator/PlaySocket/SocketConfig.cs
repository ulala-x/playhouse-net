namespace PlayHouse.Communicator.PlaySocket;

public class SocketConfig(string nid, string bindEndpoint, PlaySocketConfig playSocketConfig)
{
    public PlaySocketConfig PlaySocketConfig { get; set; } = playSocketConfig;
    public string Nid { get; internal set; } = nid;
    public string BindEndpoint { get; internal set; } = bindEndpoint;
}