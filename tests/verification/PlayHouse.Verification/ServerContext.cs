using Microsoft.AspNetCore.Builder;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Core.Play.Bootstrap;

namespace PlayHouse.Verification;

/// <summary>
/// 프로그램 전체에서 공유하는 서버/클라이언트 컨텍스트
/// </summary>
public class ServerContext
{
    public PlayServer PlayServer { get; set; } = null!;
    public ApiServer ApiServer1 { get; set; } = null!;
    public ApiServer ApiServer2 { get; set; } = null!;
    public PlayHouse.Connector.Connector Connector { get; set; } = null!;
    public WebApplication? HttpApp { get; set; }
    public int TcpPort { get; set; }
    public int ApiServer1HttpPort { get; set; }

    // Server IDs for tests
    public string PlayServerId { get; set; } = "play-1";
    public string ApiServer1Id { get; set; } = "api-1";
    public string ApiServer2Id { get; set; } = "api-2";

    // DI PlayServer (조건부)
    public PlayServer? DIPlayServer { get; set; }
    public IServiceProvider? DIServiceProvider { get; set; }
    public int DITcpPort { get; set; }

    // 프로토콜별 테스트용 포트
    public int TcpTlsPort { get; set; }
    public int WebSocketPort { get; set; }

    // 프로토콜별 서버 인스턴스 (조건부)
    public PlayServer? TlsPlayServer { get; set; }
    public PlayServer? WebSocketPlayServer { get; set; }
    public WebApplication? WebSocketHttpApp { get; set; }
}
