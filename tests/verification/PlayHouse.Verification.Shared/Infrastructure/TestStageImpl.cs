#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Verification.Shared.Proto;

namespace PlayHouse.Verification.Shared.Infrastructure;

/// <summary>
/// E2E 검증용 Stage 구현 (Client Response Only).
/// 상태 기록 없이 순수하게 응답만 생성하는 핸들러로 구현됨.
/// </summary>
/// <remarks>
/// Client Response Only 원칙:
/// - ❌ Static collections, instance tracking 금지
/// - ❌ ReceivedMsgIds, OnCreateCalled 같은 상태 기록 금지
/// - ✅ 응답 패킷만 생성하는 순수 핸들러
/// - ✅ 타이머/AsyncBlock 콜백은 Stage 기능이므로 유지
/// </remarks>
public class TestStageImpl : IStage
{
    public IStageSender StageSender { get; }

    public TestStageImpl(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        return Task.FromResult<(bool, IPacket)>((true, CPacket.Empty("CreateStageReply")));
    }

    public Task OnPostCreate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        return Task.CompletedTask;
    }

    public Task<bool> OnJoinStage(IActor actor)
    {
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 클라이언트(Actor)로부터 메시지 수신 시 처리.
    /// </summary>
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "EchoRequest":
                await HandleEchoRequest(actor, packet);
                break;

            case "FailRequest":
                actor.ActorSender.Reply(500);
                break;

            case "NoResponseRequest":
                // 의도적으로 응답하지 않음 (타임아웃 테스트용)
                break;

            case "BroadcastTrigger":
                await HandleBroadcastTrigger(actor, packet);
                break;

            case "StatusRequest":
                await HandleStatusRequest(actor);
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

            case "TriggerApiDirectEcho":
                HandleTriggerApiDirectEcho(actor, packet);
                break;

            case "TriggerRequestToApiRequest":
                await HandleTriggerRequestToApi(actor, packet);
                break;

            case "TriggerRequestToApiCallbackRequest":
                HandleTriggerRequestToApiCallback(actor, packet);
                break;

            case "TriggerAsyncBlockSendToApiRequest":
                HandleTriggerAsyncBlockSendToApi(actor, packet);
                break;

            case "TriggerAsyncBlockRequestToApiRequest":
                HandleTriggerAsyncBlockRequestToApi(actor, packet);
                break;

            case "BenchmarkRequest":
                HandleBenchmarkRequest(actor, packet);
                break;

            case "TriggerBenchmarkApiRequest":
                await HandleTriggerBenchmarkApi(actor, packet);
                break;

            case "TriggerAutoDisposeApiRequest":
                await HandleTriggerAutoDisposeApi(actor, packet);
                break;

            case "TriggerAutoDisposeStageRequest":
                await HandleTriggerAutoDisposeStage(actor, packet);
                break;

            case "StartTimerWithRequestRequest":
                await HandleStartTimerWithRequest(actor, packet);
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
        // Stage간 메시지 처리
        if (packet.MsgId.Contains("InterStageMessage"))
        {
            var request = InterStageMessage.Parser.ParseFrom(packet.Payload.DataSpan);
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
            FromAccountId = trigger.FromAccountId // 클라이언트가 보낸 값 그대로 반환
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
            ActorCount = 1, // 고정값 (상태 추적 안 함)
            UptimeSeconds = 100,
            StageType = StageSender.StageType
        };

        actor.ActorSender.Reply(CPacket.Of(statusReply));
        return Task.CompletedTask;
    }

    private Task HandleStartRepeatTimer(IActor actor, IPacket packet)
    {
        var request = StartRepeatTimerRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var tickNumber = 0;

        var timerId = StageSender.AddRepeatTimer(
            TimeSpan.FromMilliseconds(request.InitialDelayMs),
            TimeSpan.FromMilliseconds(request.IntervalMs),
            () =>
            {
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

        actor.ActorSender.Reply(CPacket.Of(new StartTimerReply { TimerId = timerId, Success = true }));
        return Task.CompletedTask;
    }

    private Task HandleStartCountTimer(IActor actor, IPacket packet)
    {
        var request = StartCountTimerRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var tickNumber = 0;

        var timerId = StageSender.AddCountTimer(
            TimeSpan.FromMilliseconds(request.InitialDelayMs),
            TimeSpan.FromMilliseconds(request.IntervalMs),
            request.Count,
            () =>
            {
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

        actor.ActorSender.Reply(CPacket.Of(new StartTimerReply { TimerId = timerId, Success = true }));
        return Task.CompletedTask;
    }

    private void HandleAsyncBlock(IActor actor, IPacket packet)
    {
        var request = AsyncBlockRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        long preThreadId = 0;
        string preResult = "";

        StageSender.AsyncIO(
            async () =>
            {
                preThreadId = Environment.CurrentManagedThreadId;

                // 외부 I/O 시뮬레이션
                await Task.Delay(request.DelayMs);
                preResult = $"pre_completed_{request.Operation}";

                return preResult;
            },
            (result) =>
            {
                var postThreadId = Environment.CurrentManagedThreadId;
                var postResult = $"post_completed_{result}";

                var reply = new AsyncBlockReply
                {
                    PreResult = preResult,
                    PostResult = postResult,
                    PreThreadId = preThreadId,
                    PostThreadId = postThreadId,
                    Sequence = request.Sequence
                };

                // AsyncBlock의 post 콜백에서는 SendToClient로 Push 메시지 전송
                actor.ActorSender.SendToClient(CPacket.Of(reply));
                return Task.CompletedTask;
            });

        // 즉시 수락 응답 전송
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

        var targetNid = string.IsNullOrEmpty(request.TargetNid) ? "1" : request.TargetNid;

        // Callback 버전 RequestToStage 호출
        StageSender.RequestToStage(
            targetNid,
            request.TargetStageId,
            CPacket.Of(interStageMsg),
            (errorCode, reply) =>
            {
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
                    actor.ActorSender.SendToClient(CPacket.Of(new TriggerRequestToStageCallbackReply
                    {
                        Response = $"Error: {errorCode}"
                    }));
                }
            });

        // 즉시 수락 응답 전송
        actor.ActorSender.Reply(CPacket.Empty("TriggerRequestToStageCallbackAccepted"));
    }

    private void HandleTriggerApiDirectEcho(IActor actor, IPacket packet)
    {
        const string apiNid = "api-1";
        var request = new ApiDirectEchoRequest { Message = "Verify StageId" };
        StageSender.SendToApi(apiNid, CPacket.Of(request));

        actor.ActorSender.Reply(CPacket.Empty("TriggerApiDirectEchoAccepted"));
    }

    private void HandleTriggerSendToApi(IActor actor, IPacket packet)
    {
        var request = TriggerSendToApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        const string apiNid = "api-1";
        var apiMsg = new ApiEchoRequest { Content = request.Message };
        StageSender.SendToApi(apiNid, CPacket.Of(apiMsg));

        actor.ActorSender.Reply(CPacket.Of(new TriggerSendToApiReply { Success = true }));
    }

    private async Task HandleTriggerRequestToApi(IActor actor, IPacket packet)
    {
        var request = TriggerRequestToApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        const string apiNid = "api-1";
        var apiMsg = new ApiEchoRequest { Content = request.Query };
        var response = await StageSender.RequestToApi(apiNid, CPacket.Of(apiMsg));

        var apiReply = ApiEchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        actor.ActorSender.Reply(CPacket.Of(new TriggerRequestToApiReply { ApiResponse = apiReply.Content }));
    }

    private void HandleTriggerRequestToApiCallback(IActor actor, IPacket packet)
    {
        var request = TriggerRequestToApiCallbackRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        const string apiNid = "api-1";
        var apiMsg = new ApiEchoRequest { Content = request.Query };

        // Callback 버전 RequestToApi 호출
        StageSender.RequestToApi(
            apiNid,
            CPacket.Of(apiMsg),
            (errorCode, reply) =>
            {
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
                    actor.ActorSender.SendToClient(CPacket.Of(new TriggerRequestToApiCallbackReply
                    {
                        ApiResponse = $"Error: {errorCode}"
                    }));
                }
            });

        // 즉시 수락 응답 전송
        actor.ActorSender.Reply(CPacket.Empty("TriggerRequestToApiCallbackAccepted"));
    }

    private void HandleBenchmarkRequest(IActor actor, IPacket packet)
    {
        var request = BenchmarkRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 지정된 크기의 페이로드 생성
        var payload = new byte[request.ResponseSize];
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

    private void HandleTriggerAsyncBlockRequestToApi(IActor actor, IPacket packet)
    {
        var request = TriggerAsyncBlockRequestToApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        StageSender.AsyncIO(
            async () =>
            {
                const string apiNid = "api-1";
                var apiMsg = new ApiEchoRequest { Content = request.Query };

                using var response = await StageSender.RequestToApi(apiNid, CPacket.Of(apiMsg)).ConfigureAwait(false);
                var apiReply = ApiEchoReply.Parser.ParseFrom(response.Payload.DataSpan);

                return apiReply.Content;
            },
            (result) =>
            {
                var apiContent = (string)result!;

                var reply = new TriggerAsyncBlockRequestToApiReply
                {
                    ApiResponse = apiContent,
                    PostBlockCalled = true
                };

                actor.ActorSender.SendToClient(CPacket.Of(reply));
                return Task.CompletedTask;
            });

        // 즉시 수락 응답 전송
        actor.ActorSender.Reply(CPacket.Empty("TriggerAsyncBlockRequestToApiAccepted"));
    }

    private void HandleTriggerAsyncBlockSendToApi(IActor actor, IPacket packet)
    {
        var request = TriggerAsyncBlockSendToApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        StageSender.AsyncIO(
            async () =>
            {
                const string apiNid = "api-1";
                var apiMsg = new ApiEchoRequest { Content = request.Message };

                StageSender.SendToApi(apiNid, CPacket.Of(apiMsg));

                await Task.CompletedTask;
                return true;
            },
            (result) =>
            {
                return Task.CompletedTask;
            });

        // 즉시 수락 응답
        actor.ActorSender.Reply(CPacket.Empty("TriggerAsyncBlockSendToApiAccepted"));
    }

    private async Task HandleTriggerBenchmarkApi(IActor actor, IPacket packet)
    {
        var request = TriggerBenchmarkApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        const string apiNid = "bench-api-1";
        var apiRequest = new BenchmarkApiRequest
        {
            Sequence = request.Sequence,
            ResponseSize = request.ResponseSize
        };

        var gcGen0Before = GC.CollectionCount(0);
        var gcGen1Before = GC.CollectionCount(1);
        var gcGen2Before = GC.CollectionCount(2);

        var count = request.Count > 0 ? request.Count : 1;
        var elapsedTicksList = new List<long>(count);
        var memoryAllocatedList = new List<long>(count);

        for (int i = 0; i < count; i++)
        {
            var memoryBefore = GC.GetTotalAllocatedBytes(precise: false);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await StageSender.RequestToApi(apiNid, CPacket.Of(apiRequest));
            sw.Stop();

            var memoryAfter = GC.GetTotalAllocatedBytes(precise: false);

            elapsedTicksList.Add(sw.ElapsedTicks);
            memoryAllocatedList.Add(memoryAfter - memoryBefore);
        }

        var gcGen0After = GC.CollectionCount(0);
        var gcGen1After = GC.CollectionCount(1);
        var gcGen2After = GC.CollectionCount(2);

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

    private async Task HandleTriggerAutoDisposeApi(IActor actor, IPacket packet)
    {
        var request = TriggerAutoDisposeApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        const string apiNid = "api-1";
        var apiMsg = new ApiEchoRequest { Content = request.Query };
        var response = await StageSender.RequestToApi(apiNid, CPacket.Of(apiMsg));

        var apiReply = ApiEchoReply.Parser.ParseFrom(response.Payload.DataSpan);

        actor.ActorSender.Reply(CPacket.Of(new TriggerAutoDisposeApiReply
        {
            ApiResponse = apiReply.Content
        }));
    }

    private async Task HandleTriggerAutoDisposeStage(IActor actor, IPacket packet)
    {
        var request = TriggerAutoDisposeStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var interStageMsg = new InterStageMessage
        {
            FromStageId = StageSender.StageId,
            Content = request.Query
        };

        var targetNid = string.IsNullOrEmpty(request.TargetNid) ? "1" : request.TargetNid;
        var response = await StageSender.RequestToStage(
            targetNid,
            request.TargetStageId,
            CPacket.Of(interStageMsg));

        var interStageReply = InterStageReply.Parser.ParseFrom(response.Payload.DataSpan);

        actor.ActorSender.Reply(CPacket.Of(new TriggerAutoDisposeStageReply
        {
            Response = interStageReply.Response
        }));
    }

    private Task HandleStartTimerWithRequest(IActor actor, IPacket packet)
    {
        var request = StartTimerWithRequestRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var timerId = StageSender.AddCountTimer(
            TimeSpan.FromMilliseconds(request.DelayMs),
            TimeSpan.FromMilliseconds(50),
            1,
            async () =>
            {
                const string apiNid = "api-1";
                var apiMsg = new TimerApiRequest { Content = "timer_test" };

                try
                {
                    var response = await StageSender.RequestToApi(apiNid, CPacket.Of(apiMsg));

                    var apiReply = TimerApiReply.Parser.ParseFrom(response.Payload.DataSpan);

                    actor.ActorSender.SendToClient(CPacket.Of(new TimerRequestResultNotify
                    {
                        Result = apiReply.Content,
                        Success = true
                    }));
                }
                catch (Exception)
                {
                    actor.ActorSender.SendToClient(CPacket.Of(new TimerRequestResultNotify
                    {
                        Result = "error",
                        Success = false
                    }));
                }
            });

        actor.ActorSender.Reply(CPacket.Of(new StartTimerWithRequestReply { TimerId = timerId }));
        return Task.CompletedTask;
    }
}
