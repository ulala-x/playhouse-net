// 현재 작업중인 시스템의 구현을 변경할예정
// 기존 Play (Playhouse) 서버만 있던것을  Play (Stage관리) 서버와  Api ( play 서버로부터 요청 처리 stateless) 서버로 두개로 관리
// 기존 RestApi 로 요청 받던 부분을 , Zeromq(netmq) 를 이용해서 서버간 통신으로 처리,  rest api 처리 부분은 삭제
//  Stage 생성은  api 서버에서 요청으로 생성되고, 방입장은 client 가 접속후 인증이 성공하면 방입장이 가능하도록 수정
// 아래 인터페이스를 제공해야 하는데 어떻게 구현되어야 할지는 D:\project\kairos\playhouse\playhouse-net\PlayHouse 여기에 이미 다 구현이 이 되어 있음
//  단 완전히 똑같지는 않음 같은것도 있고 비슷한것도 있고 동작의 정의가 달라진것도 있음 D:\project\kairos\playhouse\playhouse-net\PlayHouse 이 시스템에서 Session 을 삭제하고 Session 의 client 연결관리 기능이 Play  서버와 합쳐지고 , API 서버 기능은 단순 요청 처리로 간소화 된다고 생각하면됨
// Zeormq 의 사용과 서버 관련 내용은 D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\PlaySocket, D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime 이쪽 코드를 집중 분석하면되고 아마 거의 모든 코드를 그대로 재사용 가능할것이라고 예상


////////// packet ///////////////////////////////////////////

public interface IPacket : IDisposable
{
public string MsgId { get; }

    public IPayload Payload { get; }
    //public IPacket Copy();
    //public T Parse<T>();
}

public interface IPayload : IDisposable
{
public ReadOnlyMemory<byte> Data { get; }
public ReadOnlySpan<byte> DataSpan => Data.Span;
}


public class ProtoPayload(IMessage proto) : IPayload
{
public ReadOnlyMemory<byte> Data => proto.ToByteArray();

    public void Dispose()
    {
    }

    public IMessage GetProto()
    {
        return proto;
    }
}

//////////////////////////////// system ///////////////////////////////////////////
public enum ServerState
{
RUNNING,
PAUSE,
DISABLE
}

public interface IServerInfo
{
string GetBindEndpoint();
string GetNid();
int GetServerId();
ServiceType GetServiceType();
ushort GetServiceId();
ServerState GetState();
long GetLastUpdate();
int GetActorCount();  // 현재 Stage/Actor 수 (로드밸런싱용)
}

public interface ISystemPanel
{
IServerInfo GetServerInfo();
IServerInfo GetServerInfoBy(ushort serviceId);
IServerInfo GetServerInfoByNid(string nid);
IList<IServerInfo> GetServers();
void Pause();
void Resume();
Task ShutdownASync();
ServerState GetServerState();

    public static string MakeNid(ushort serviceId, int serverId)
    {
        return $"{serviceId}:{serverId}";
    }
}

public interface ISystemController
{
void Handles(ISystemHandlerRegister handlerRegister);

    Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo);
}



public delegate Task TimerCallbackTask();

public interface ISender
{
ushort ServiceId { get; }
void SendToApi(string apiNid, IPacket packet);
void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback);
Task<IPacket> RequestToApi(string apiNid, IPacket packet);

    void SendToStage(string playNid, long stageId, IPacket packet);
    void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback);
    Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet);

    void Reply(ushort errorCode);
    void Reply(IPacket reply);
}


//////////////////////////////// Play  ///////////////////////////////////////////
// Play Server 로 Stage 를 생성하고 관리한다. Client 가 Play 서버에 접속하고 Stage 에서 여러 유저들과 realtime 으로 메시지를 주고 받을수 있다.
// Api 서버의 요청으로 Stage 는 생성된다. , Client 가 접속하면 인증후 stage 에 입장하게 하도록 한다.
// Play Server 에서 Api 서버로 메시지를 보낼수 있고 다른 Play Server 의 Stage 로도 메시지를 보낼수 있다.

public interface IActorSender : ISender
{
    string AccountId { get; set; } // OnAuthenticate 가 성공이면 컨텐츠 개발자가 accountId 값을 설정해야 한다.
                                   // OnAuthenticate 가 성공했는데 AccountId 가 "" 이면 exception 발생 및 접속 끊김 처리
    void LeaveStage();
    void SendToClient(IPacket packet);
}

public interface IActor
{
IActorSender ActorSender { get; }
Task OnCreate();
Task OnDestroy();

    Task<bool> OnAuthenticate(IPacket authPacket);
    Task OnPostAuthenticate(); //OnAuthenticate 가 성공하면 호출됨, 주로 여기서 actor 에 대한 정보를 api 서버에 요청해서 받아 올것
}


public delegate Task<object> AsyncPreCallback();

public delegate Task AsyncPostCallback(object result);


public interface IStageSender : ISender
{
public long StageId { get; }
public string StageType { get; }

    // 타이머 관리
    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback);
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback);
    void CancelTimer(long timerId);
    bool HasTimer(long timerId);

    // Stage 관리
    void CloseStage(); // Stage를 종료하고 모든 타이머를 취소한다. OnDestory() 호출됨

    // 비동기 블록 (외부 I/O를 Event Loop 외부에서 처리 후 결과를 안전하게 전달)
    void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);

    // Note: Reply()는 ISender에서 상속받음
}

public interface IStage
{
public IStageSender StageSender { get; }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet);
    public Task OnPostCreate();
    public Task OnDestory();

    public Task<bool> OnJoinStage(IActor actor); // IActor의 OnPostAuthenticate 함수가 호출되고 나서 호출됨. false 반환 시 connection 끊김, IActor 삭제됨
    public Task OnPostJoinStage(IActor actor);   // OnJoinStage에서 true 반환 시 호출됨
    // OnLeaveStage 불필요: 퇴장은 컨텐츠 로직에서 처리 후 actor.ActorSender.LeaveStage() 호출

    ValueTask OnConnectionChanged(IActor actor, bool isConnected);

    // 이 두 함수는 동일 이벤트 루프를 사용해야 한다.
    public Task OnDispatch(IActor actor, IPacket packet); // Client에서 보낸 메시지 → 이 콜백 호출
    public Task OnDispatch(IPacket packet);               // ISender.RequestToStage() 호출 시 → 이 콜백 호출 (서버 간 통신)
}


//////////////////////////////// API  ///////////////////////////////////////////
// 웹서버와 같이 사용되는 모듈 ,내부적으로는 zeromq router 의 서버 소켓이 bind 되어서  Play 서버의 요청을 받거나 Play 서버로 요청을 보낼수 있다.
// Play Server 에 Stage 생성을 요청 할수 있다.
// 다른 API 서버에게도 Message 를 보낼수 있다.



public class CreateStageResult(bool result, IPacket createStageRes) : StageResult(result)
{
public IPacket CreateStageRes { get; } = createStageRes;
}
public class GetOrCreateStageResult(bool result, bool isCreated, IPacket createStageRes)
: StageResult(result)
{
public bool IsCreated { get; } = isCreated;
public IPacket CreateStageRes { get; } = createStageRes;
}

public interface IApiSender :  ISender
{
Task<CreateStageResult> CreateStage(string playNid, string stageType, long stageId, IPacket packet);
Task<CreateJoinStageResult> GetOrCreateStage(string playNid, string stageType, long stageId,IPacket createPacket, IPacket joinPacke );
}

public delegate Task ApiHandler(IPacket packet, IApiSender apiSender);

public interface IHandlerRegister
{
void Add(string msgId, ApiHandler handler);
}

public interface IApiController
{
void Handles(IHandlerRegister handlerRegister);
}



