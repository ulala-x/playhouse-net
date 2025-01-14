using PlayHouse.Production.Shared;

namespace PlayHouse.Production.Session;

public class SessionOption
{
    public Func<ISessionSender, ISessionUser>? SessionUserFactory = null;
    public int ClientIdleTimeoutMSec { get; set; } = 0; //30000  0인경우 idle확인 안함

    public List<string> Urls { get; set; } = new();
    public int SessionPort { get; set; } = 0;
    public bool UseWebSocket { get; set; } = false;
}