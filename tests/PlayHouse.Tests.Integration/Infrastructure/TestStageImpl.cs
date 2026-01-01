#nullable enable

using System.Collections.Concurrent;
using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Tests.Integration.Proto;

namespace PlayHouse.Tests.Integration.Infrastructure;

/// <summary>
/// E2E 테스트용 Stage 구현.
/// Connector에서 전송한 메시지를 처리하고 적절한 응답을 반환합니다.
/// </summary>
/// <remarks>
/// 지원하는 메시지:
/// - EchoRequest → EchoReply (동일 내용 반환)
/// - FailRequest → 에러코드 500 반환
/// - BroadcastTrigger → SendToClient로 Push 전송
/// - NoResponseRequest → 응답 없음 (타임아웃 테스트용)
/// </remarks>
public class TestStageImpl : IStage
{
    public IStageSender StageSender { get; }

    // Static 필드
    public static ConcurrentBag<TestStageImpl> Instances { get; } = new();
    public static ConcurrentBag<string> AllReceivedMsgIds { get; } = new();
    public static int OnDispatchCallCount => _onDispatchCallCount;
    public static int TimerCallbackCount => _timerCallbackCount;
    public static int AsyncPreCallbackCount => _asyncPreCallbackCount;
    public static int AsyncPostCallbackCount => _asyncPostCallbackCount;
    public static ConcurrentBag<string> InterStageReceivedMsgIds { get; } = new();
    public static int InterStageMessageCount => _interStageMessageCount;
    public static int RequestToStageCallbackCount => _requestToStageCallbackCount;
    public static int RequestToApiCallbackCount => _requestToApiCallbackCount;

    private static readonly ConcurrentDictionary<long, TestStageImpl> _stageIdMap = new();
    private static readonly ConcurrentDictionary<long, int> _timerCallbackCountByStageId = new();
    private static int _onDispatchCallCount;
    private static int _timerCallbackCount;
    private static int _asyncPreCallbackCount;
    private static int _asyncPostCallbackCount;
    private static int _interStageMessageCount;
    private static int _requestToStageCallbackCount;
    private static int _requestToApiCallbackCount;

    // 테스트 검증용 데이터
    public List<string> ReceivedMsgIds { get; } = new();
    public List<IActor> JoinedActors { get; } = new();
    public List<(IActor actor, bool isConnected)> ConnectionChanges { get; } = new();
    public bool OnCreateCalled { get; private set; }
    public bool OnDestroyCalled { get; private set; }
    public bool OnPostCreateCalled { get; private set; }
    public bool OnPostJoinStageCalled { get; private set; }
    public IPacket? LastCreatePacket { get; private set; }

    /// <summary>
    /// StageId로 특정 TestStageImpl 인스턴스를 조회합니다.
    /// </summary>
    public static TestStageImpl? GetByStageId(long stageId)
        => _stageIdMap.TryGetValue(stageId, out var instance) ? instance : null;

    /// <summary>
    /// 특정 StageId에 대한 타이머 콜백 호출 횟수를 반환합니다.
    /// </summary>
    public static int GetTimerCallbackCount(long stageId)
        => _timerCallbackCountByStageId.TryGetValue(stageId, out var count) ? count : 0;

    public static void ResetAll()
    {
        while (Instances.TryTake(out _)) { }
        while (AllReceivedMsgIds.TryTake(out _)) { }
        while (InterStageReceivedMsgIds.TryTake(out _)) { }
        _stageIdMap.Clear();
        _timerCallbackCountByStageId.Clear();
        Interlocked.Exchange(ref _onDispatchCallCount, 0);
        Interlocked.Exchange(ref _timerCallbackCount, 0);
        Interlocked.Exchange(ref _asyncPreCallbackCount, 0);
        Interlocked.Exchange(ref _asyncPostCallbackCount, 0);
        Interlocked.Exchange(ref _interStageMessageCount, 0);
        Interlocked.Exchange(ref _requestToStageCallbackCount, 0);
        Interlocked.Exchange(ref _requestToApiCallbackCount, 0);
    }

    public TestStageImpl(IStageSender stageSender)
    {
        StageSender = stageSender;
        Instances.Add(this);
        _stageIdMap[stageSender.StageId] = this;
    }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        OnCreateCalled = true;
        LastCreatePacket = packet;
        return Task.FromResult<(bool, IPacket)>((true, CPacket.Empty("CreateStageReply")));
    }

    public Task OnPostCreate()
    {
        OnPostCreateCalled = true;
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        OnDestroyCalled = true;
        return Task.CompletedTask;
    }

    public Task<bool> OnJoinStage(IActor actor)
    {
        JoinedActors.Add(actor);
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        OnPostJoinStageCalled = true;
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        ConnectionChanges.Add((actor, isConnected));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 클라이언트(Actor)로부터 메시지 수신 시 처리.
    /// </summary>
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);
        AllReceivedMsgIds.Add(packet.MsgId);

        switch (packet.MsgId)
        {
            case "EchoRequest":
                await HandleEchoRequest(actor, packet);
                break;

            case "FailRequest":
                // 에러 응답
                actor.ActorSender.Reply(500);
                break;

            case "BroadcastTrigger":
                // Push 메시지 전송 트리거
                await HandleBroadcastTrigger(actor, packet);
                break;

            case "NoResponseRequest":
                // 의도적으로 응답하지 않음 (타임아웃 테스트용)
                break;

            case "StatusRequest":
                await HandleStatusRequest(actor);
                break;

            case "TriggerSendToClient":
                // IStageSender.SendToClient 트리거
                await HandleSendToClientTrigger(actor, packet);
                break;

            case "StartRepeatTimerRequest":
                await HandleStartRepeatTimer(actor, packet);
                break;

            case "StartCountTimerRequest":
                await HandleStartCountTimer(actor, packet);
                break;

            case "AsyncBlockRequest":
                HandleAsyncBlock(actor, packet);
                break;

            case "CloseStageRequest":
                HandleCloseStage(actor, packet);
                break;

            case "GetAccountIdRequest":
                HandleGetAccountId(actor);
                break;

            case "LeaveStageRequest":
                HandleLeaveStage(actor, packet);
                break;

            case "TriggerSendToStageRequest":
                HandleTriggerSendToStage(actor, packet);
                break;

            case "TriggerRequestToStageRequest":
                await HandleTriggerRequestToStage(actor, packet);
                break;

            case "TriggerRequestToStageCallbackRequest":
                HandleTriggerRequestToStageCallback(actor, packet);
                break;

            case "TriggerSendToApiRequest":
                HandleTriggerSendToApi(actor, packet);
                break;

            case "TriggerRequestToApiRequest":
                await HandleTriggerRequestToApi(actor, packet);
                break;

            case "TriggerRequestToApiCallbackRequest":
                HandleTriggerRequestToApiCallback(actor, packet);
                break;

            case "BenchmarkRequest":
                HandleBenchmarkRequest(actor, packet);
                break;

            case "TriggerBenchmarkApiRequest":
                await HandleTriggerBenchmarkApi(actor, packet);
                break;

            default:
                // 기본 성공 응답
                actor.ActorSender.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }
    }

    /// <summary>
    /// 서버 간 메시지 수신 시 처리.
    /// </summary>
    public Task OnDispatch(IPacket packet)
    {
        ReceivedMsgIds.Add(packet.MsgId);

        // Stage간 메시지 처리
        // MsgId는 전체 네임스페이스를 포함할 수 있으므로 EndsWith로 확인
        if (packet.MsgId.Contains("InterStageMessage"))
        {
            Interlocked.Increment(ref _interStageMessageCount);
            InterStageReceivedMsgIds.Add(packet.MsgId);

            var request = InterStageMessage.Parser.ParseFrom(packet.Payload.DataSpan);
            // InterStageReply로 응답 (RequestToStage인 경우)
            StageSender.Reply(CPacket.Of(new InterStageReply { Response = $"Echo: {request.Content}" }));
        }

        return Task.CompletedTask;
    }

    private Task HandleEchoRequest(IActor actor, IPacket packet)
    {
        var echoRequest = EchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var echoReply = new EchoReply
        {
            Content = echoRequest.Content,
            Sequence = echoRequest.Sequence,
            ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        actor.ActorSender.Reply(CPacket.Of(echoReply));
        return Task.CompletedTask;
    }

    private Task HandleBroadcastTrigger(IActor actor, IPacket packet)
    {
        var trigger = BroadcastNotify.Parser.ParseFrom(packet.Payload.DataSpan);
        var pushMessage = new BroadcastNotify
        {
            EventType = trigger.EventType,
            Data = trigger.Data,
            FromAccountId = long.Parse(actor.ActorSender.AccountId)
        };

        // SendToClient로 Push 전송
        actor.ActorSender.SendToClient(CPacket.Of(pushMessage));

        // 성공 응답
        actor.ActorSender.Reply(CPacket.Empty("BroadcastTriggerReply"));
        return Task.CompletedTask;
    }

    private Task HandleStatusRequest(IActor actor)
    {
        var statusReply = new StatusReply
        {
            ActorCount = JoinedActors.Count,
            UptimeSeconds = 100,
            StageType = StageSender.StageType
        };

        actor.ActorSender.Reply(CPacket.Of(statusReply));
        return Task.CompletedTask;
    }

    private Task HandleSendToClientTrigger(IActor actor, IPacket packet)
    {
        // IStageSender.SendToClient 테스트용
        // 요청을 받으면 Push 메시지를 보내고 성공 응답
        var pushNotify = new BroadcastNotify
        {
            EventType = "push_test",
            Data = "triggered_by_sendtoclient",
            FromAccountId = 0
        };

        actor.ActorSender.SendToClient(CPacket.Of(pushNotify));
        actor.ActorSender.Reply(CPacket.Empty("TriggerSendToClientReply"));
        return Task.CompletedTask;
    }

    private Task HandleStartRepeatTimer(IActor actor, IPacket packet)
    {
        var request = StartRepeatTimerRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var tickNumber = 0;
        var stageId = StageSender.StageId;

        var timerId = StageSender.AddRepeatTimer(
            TimeSpan.FromMilliseconds(request.InitialDelayMs),
            TimeSpan.FromMilliseconds(request.IntervalMs),
            () =>
            {
                Interlocked.Increment(ref _timerCallbackCount);
                _timerCallbackCountByStageId.AddOrUpdate(stageId, 1, (_, c) => c + 1);
                tickNumber++;
                var notify = new TimerTickNotify
                {
                    TickNumber = tickNumber,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    TimerType = "repeat"
                };
                actor.ActorSender.SendToClient(CPacket.Of(notify));
                return Task.CompletedTask;
            });

        actor.ActorSender.Reply(CPacket.Of(new StartTimerReply { TimerId = timerId }));
        return Task.CompletedTask;
    }

    private Task HandleStartCountTimer(IActor actor, IPacket packet)
    {
        var request = StartCountTimerRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var tickNumber = 0;
        var stageId = StageSender.StageId;

        var timerId = StageSender.AddCountTimer(
            TimeSpan.FromMilliseconds(request.InitialDelayMs),
            TimeSpan.FromMilliseconds(request.IntervalMs),
            request.Count,
            () =>
            {
                Interlocked.Increment(ref _timerCallbackCount);
                _timerCallbackCountByStageId.AddOrUpdate(stageId, 1, (_, c) => c + 1);
                tickNumber++;
                var notify = new TimerTickNotify
                {
                    TickNumber = tickNumber,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    TimerType = "count"
                };
                actor.ActorSender.SendToClient(CPacket.Of(notify));
                return Task.CompletedTask;
            });

        actor.ActorSender.Reply(CPacket.Of(new StartTimerReply { TimerId = timerId }));
        return Task.CompletedTask;
    }

    private void HandleAsyncBlock(IActor actor, IPacket packet)
    {
        var request = AsyncBlockRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        long preThreadId = 0;
        string preResult = "";

        StageSender.AsyncBlock(
            async () =>
            {
                Interlocked.Increment(ref _asyncPreCallbackCount);
                preThreadId = Environment.CurrentManagedThreadId;

                // 외부 I/O 시뮬레이션
                await Task.Delay(request.DelayMs);
                preResult = $"pre_completed_{request.Operation}";

                return preResult;
            },
            (result) =>
            {
                Interlocked.Increment(ref _asyncPostCallbackCount);
                var postThreadId = Environment.CurrentManagedThreadId;
                var postResult = $"post_completed_{result}";

                var reply = new AsyncBlockReply
                {
                    PreResult = preResult,
                    PostResult = postResult,
                    PreThreadId = preThreadId,
                    PostThreadId = postThreadId
                };

                // AsyncBlock의 post 콜백에서는 CurrentHeader가 이미 클리어됨
                // 따라서 SendToClient로 Push 메시지 전송 (MsgSeq=0)
                actor.ActorSender.SendToClient(CPacket.Of(reply));
                return Task.CompletedTask;
            });

        // 즉시 수락 응답 전송 (CurrentHeader가 아직 유효할 때)
        actor.ActorSender.Reply(CPacket.Empty("AsyncBlockAccepted"));
    }

    private void HandleCloseStage(IActor actor, IPacket packet)
    {
        var request = CloseStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        actor.ActorSender.Reply(CPacket.Of(new CloseStageReply { Success = true }));
        StageSender.CloseStage();
    }

    private void HandleGetAccountId(IActor actor)
    {
        var reply = new GetAccountIdReply
        {
            AccountId = actor.ActorSender.AccountId
        };
        actor.ActorSender.Reply(CPacket.Of(reply));
    }

    private void HandleLeaveStage(IActor actor, IPacket packet)
    {
        var request = LeaveStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        // 먼저 Reply를 보낸 후 LeaveStage 호출 (순서 중요)
        actor.ActorSender.Reply(CPacket.Of(new LeaveStageReply { Success = true }));
        actor.ActorSender.LeaveStage();
    }

    private void HandleTriggerSendToStage(IActor actor, IPacket packet)
    {
        var request = TriggerSendToStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // Stage간 메시지 전송
        var interStageMsg = new InterStageMessage
        {
            FromStageId = StageSender.StageId,
            Content = request.Message
        };

        // TargetNid가 지정되지 않으면 기본값 "1" 사용 (이전 호환성)
        var targetNid = string.IsNullOrEmpty(request.TargetNid) ? "1" : request.TargetNid;
        StageSender.SendToStage(targetNid, request.TargetStageId, CPacket.Of(interStageMsg));

        // 성공 응답
        actor.ActorSender.Reply(CPacket.Of(new TriggerSendToStageReply { Success = true }));
    }

    private async Task HandleTriggerRequestToStage(IActor actor, IPacket packet)
    {
        var request = TriggerRequestToStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var interStageMsg = new InterStageMessage
        {
            FromStageId = StageSender.StageId,
            Content = request.Query
        };

        // TargetNid가 지정되지 않으면 기본값 "1" 사용 (이전 호환성)
        var targetNid = string.IsNullOrEmpty(request.TargetNid) ? "1" : request.TargetNid;
        var response = await StageSender.RequestToStage(
            targetNid,
            request.TargetStageId,
            CPacket.Of(interStageMsg));

        // Stage B의 응답을 클라이언트에 전달
        var interStageReply = InterStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        actor.ActorSender.Reply(CPacket.Of(new TriggerRequestToStageReply { Response = interStageReply.Response }));
    }

    private void HandleTriggerRequestToStageCallback(IActor actor, IPacket packet)
    {
        var request = TriggerRequestToStageCallbackRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var interStageMsg = new InterStageMessage
        {
            FromStageId = StageSender.StageId,
            Content = request.Query
        };

        // TargetNid가 지정되지 않으면 기본값 "1" 사용 (이전 호환성)
        var targetNid = string.IsNullOrEmpty(request.TargetNid) ? "1" : request.TargetNid;

        // Callback 버전 RequestToStage 호출
        StageSender.RequestToStage(
            targetNid,
            request.TargetStageId,
            CPacket.Of(interStageMsg),
            (errorCode, reply) =>
            {
                // Callback 호출 횟수 증가
                Interlocked.Increment(ref _requestToStageCallbackCount);

                // Callback에서 응답을 클라이언트에 전달
                if (errorCode == 0 && reply != null)
                {
                    var interStageReply = InterStageReply.Parser.ParseFrom(reply.Payload.DataSpan);
                    actor.ActorSender.SendToClient(CPacket.Of(new TriggerRequestToStageCallbackReply
                    {
                        Response = interStageReply.Response
                    }));
                }
                else
                {
                    // 에러 발생 시 에러 응답 전송
                    actor.ActorSender.SendToClient(CPacket.Of(new TriggerRequestToStageCallbackReply
                    {
                        Response = $"Error: {errorCode}"
                    }));
                }
            });

        // 즉시 수락 응답 전송 (비동기 처리 시작됨을 알림)
        actor.ActorSender.Reply(CPacket.Empty("TriggerRequestToStageCallbackAccepted"));
    }

    private void HandleTriggerSendToApi(IActor actor, IPacket packet)
    {
        var request = TriggerSendToApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // API Server ServerId는 "api-1" (ApiPlayServerFixture 및 다른 테스트)
        const string apiNid = "api-1";
        var apiMsg = new ApiEchoRequest { Content = request.Message };
        StageSender.SendToApi(apiNid, CPacket.Of(apiMsg));

        actor.ActorSender.Reply(CPacket.Of(new TriggerSendToApiReply { Success = true }));
    }

    private async Task HandleTriggerRequestToApi(IActor actor, IPacket packet)
    {
        var request = TriggerRequestToApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // API Server ServerId는 "api-1" (ApiPlayServerFixture 및 다른 테스트)
        const string apiNid = "api-1";
        var apiMsg = new ApiEchoRequest { Content = request.Query };
        var response = await StageSender.RequestToApi(apiNid, CPacket.Of(apiMsg));

        var apiReply = ApiEchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        actor.ActorSender.Reply(CPacket.Of(new TriggerRequestToApiReply { ApiResponse = apiReply.Content }));
    }

    private void HandleTriggerRequestToApiCallback(IActor actor, IPacket packet)
    {
        var request = TriggerRequestToApiCallbackRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // API Server ServerId는 "api-1" (ApiPlayServerFixture 및 다른 테스트)
        const string apiNid = "api-1";
        var apiMsg = new ApiEchoRequest { Content = request.Query };

        // Callback 버전 RequestToApi 호출
        StageSender.RequestToApi(
            apiNid,
            CPacket.Of(apiMsg),
            (errorCode, reply) =>
            {
                // Callback 호출 횟수 증가
                Interlocked.Increment(ref _requestToApiCallbackCount);

                // Callback에서 응답을 클라이언트에 전달
                if (errorCode == 0 && reply != null)
                {
                    var apiReply = ApiEchoReply.Parser.ParseFrom(reply.Payload.DataSpan);
                    actor.ActorSender.SendToClient(CPacket.Of(new TriggerRequestToApiCallbackReply
                    {
                        ApiResponse = apiReply.Content
                    }));
                }
                else
                {
                    // 에러 발생 시 에러 응답 전송
                    actor.ActorSender.SendToClient(CPacket.Of(new TriggerRequestToApiCallbackReply
                    {
                        ApiResponse = $"Error: {errorCode}"
                    }));
                }
            });

        // 즉시 수락 응답 전송 (비동기 처리 시작됨을 알림)
        actor.ActorSender.Reply(CPacket.Empty("TriggerRequestToApiCallbackAccepted"));
    }

    /// <summary>
    /// 벤치마크 요청 처리 - 지정된 크기의 응답 반환
    /// </summary>
    private void HandleBenchmarkRequest(IActor actor, IPacket packet)
    {
        var request = BenchmarkRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 지정된 크기의 페이로드 생성
        var payload = new byte[request.ResponseSize];
        // 패턴으로 채우기 (압축 방지)
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        var reply = new BenchmarkReply
        {
            Sequence = request.Sequence,
            ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Payload = Google.Protobuf.ByteString.CopyFrom(payload)
        };

        actor.ActorSender.Reply(CPacket.Of(reply));
    }

    /// <summary>
    /// 벤치마크 API 요청 트리거 - Stage → API 통신 성능 측정용
    /// </summary>
    private async Task HandleTriggerBenchmarkApi(IActor actor, IPacket packet)
    {
        var request = TriggerBenchmarkApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // API 서버에 벤치마크 요청 전송
        const string apiNid = "bench-api-1";
        var apiRequest = new BenchmarkApiRequest
        {
            Sequence = request.Sequence,
            ResponseSize = request.ResponseSize
        };

        // 측정 전 상태 기록
        var gcGen0Before = GC.CollectionCount(0);
        var gcGen1Before = GC.CollectionCount(1);
        var gcGen2Before = GC.CollectionCount(2);

        // count만큼 반복하여 측정 데이터 수집
        var count = request.Count > 0 ? request.Count : 1;
        var elapsedTicksList = new List<long>(count);
        var memoryAllocatedList = new List<long>(count);

        for (int i = 0; i < count; i++)
        {
            var memoryBefore = GC.GetTotalAllocatedBytes(precise: false);

            // ★ Stage→Api 구간만 측정
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await StageSender.RequestToApi(apiNid, CPacket.Of(apiRequest));
            sw.Stop();

            var memoryAfter = GC.GetTotalAllocatedBytes(precise: false);

            elapsedTicksList.Add(sw.ElapsedTicks);
            memoryAllocatedList.Add(memoryAfter - memoryBefore);
        }

        // 측정 후 상태 기록
        var gcGen0After = GC.CollectionCount(0);
        var gcGen1After = GC.CollectionCount(1);
        var gcGen2After = GC.CollectionCount(2);

        // 모든 측정 데이터를 클라이언트에 전달
        var reply = new TriggerBenchmarkApiReply
        {
            Sequence = request.Sequence,
            GcGen0Count = gcGen0After - gcGen0Before,
            GcGen1Count = gcGen1After - gcGen1Before,
            GcGen2Count = gcGen2After - gcGen2Before
        };
        reply.ElapsedTicksList.AddRange(elapsedTicksList);
        reply.MemoryAllocatedList.AddRange(memoryAllocatedList);

        actor.ActorSender.Reply(CPacket.Of(reply));
    }
}
