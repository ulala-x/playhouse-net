#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;
using PlayHouse.Extensions.Proto;

namespace PlayHouse.E2E.Shared.Infrastructure;

/// <summary>
/// E2E 검증용 API Controller (Client Response Only).
/// 상태 기록 없이 순수하게 응답만 생성하는 핸들러로 구현됨.
/// </summary>
/// <remarks>
/// Client Response Only 원칙:
/// - ❌ Static collections, instance tracking 금지
/// - ❌ ReceivedMsgIds, OnDispatchCallCount 같은 상태 기록 금지
/// - ✅ 응답 패킷만 생성하는 순수 핸들러
/// </remarks>
public class TestApiController : IApiController
{
    private readonly ILogger<TestApiController> _logger;

    /// <summary>
    /// 벤치마크용 페이로드 사전 할당 (메모리 할당 오염 방지).
    /// </summary>
    private static readonly Dictionary<int, Google.Protobuf.ByteString> PreallocatedPayloads = new()
    {
        { 1024, CreatePayload(1024) },
        { 65536, CreatePayload(65536) },
        { 131072, CreatePayload(131072) },
        { 262144, CreatePayload(262144) }
    };

    public TestApiController()
    {
        _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TestApiController>.Instance;
    }

    private static Google.Protobuf.ByteString CreatePayload(int size)
    {
        var payload = new byte[size];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return Google.Protobuf.ByteString.CopyFrom(payload);
    }

    public void Handles(IHandlerRegister register)
    {
        _logger.LogDebug("Registering API handlers");
        register.Add(typeof(ApiEchoRequest).Name!, HandleApiEcho);
        register.Add(typeof(ApiDirectEchoRequest).Name!, HandleApiDirectEcho);
        register.Add(typeof(TriggerCreateStageRequest).Name!, HandleCreateStage);
        register.Add(typeof(TriggerGetOrCreateStageRequest).Name!, HandleGetOrCreateStage);
        register.Add(typeof(TriggerSendToApiServerRequest).Name!, HandleSendToApiServer);
        register.Add(typeof(TriggerRequestToApiServerRequest).Name!, HandleRequestToApiServer);
        register.Add(typeof(InterApiMessage).Name!, HandleInterApiMessage);
        register.Add(typeof(BenchmarkApiRequest).Name!, HandleBenchmarkApi);
        register.Add(typeof(TimerApiRequest).Name!, HandleTimerApi);
        register.Add(typeof(GetApiAccountIdRequest).Name!, HandleGetApiAccountId);

        // System message trigger handlers
        register.Add(typeof(TriggerApiSendToSystemPlayRequest).Name!, HandleApiSendToSystemPlay);
        register.Add(typeof(TriggerApiSendToSystemApiRequest).Name!, HandleApiSendToSystemApi);
        register.Add(typeof(TriggerApiRequestToSystemRequest).Name!, HandleApiRequestToSystem);
    }

    private Task HandleApiEcho(IPacket packet, IApiSender sender)
    {
        var request =  packet.Parse<ApiEchoRequest>();
        //var request = ApiEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var reply = new ApiEchoReply { Content = $"Echo: {request.Content}" };
        
        //sender.Reply(ProtoCPacketExtensions.OfProto(reply));
        sender.Reply(reply);
        return Task.CompletedTask;
    }

    private Task HandleApiDirectEcho(IPacket packet, IApiSender sender)
    {
        var request = ApiDirectEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var reply = new ApiDirectEchoReply { Message = $"Direct: {request.Message}" };

        // Note: This handler is for S2S_DirectRouting test which is skipped in verification
        // because it tests server-side routing (API→Stage SendToStage) incompatible with E2E
        // Integration test uses SendToStage; verification would need SendToClient
        // but that requires different routing context

        return Task.CompletedTask;
    }

    private async Task HandleCreateStage(IPacket packet, IApiSender sender)
    {
        var request = TriggerCreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        const string playNid = "play-1";
        var createPayload = new CreateStagePayload
        {
            StageName = request.StageType,
            MaxPlayers = 10
        };
        var result = await sender.CreateStage(
            playNid,
            request.StageType,
            request.StageId,
            ProtoCPacketExtensions.OfProto(createPayload));

        var reply = new TriggerCreateStageReply
        {
            Success = result.Result
        };
        sender.Reply(reply);
    }

    private async Task HandleGetOrCreateStage(IPacket packet, IApiSender sender)
    {
        var request = TriggerGetOrCreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        const string playNid = "play-1";
        var createPayload = new CreateStagePayload
        {
            StageName = request.StageType,
            MaxPlayers = 10
        };
        var result = await sender.GetOrCreateStage(
            playNid,
            request.StageType,
            request.StageId,
            ProtoCPacketExtensions.OfProto(createPayload));

        var reply = new TriggerGetOrCreateStageReply
        {
            Success = result.Result,
            IsCreated = result.IsCreated
        };
        sender.Reply(reply);
    }

    private Task HandleSendToApiServer(IPacket packet, IApiSender sender)
    {
        var request = TriggerSendToApiServerRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var message = new InterApiMessage
        {
            FromApiNid = "sender-api",
            Content = request.Message
        };
        sender.SendToApi(request.TargetApiNid, message);

        var reply = new TriggerSendToApiServerReply { Success = true };
        sender.Reply(reply);
        return Task.CompletedTask;
    }

    private async Task HandleRequestToApiServer(IPacket packet, IApiSender sender)
    {
        var request = TriggerRequestToApiServerRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 안정성 확보 (full suite 실행 시 타이밍 이슈 방지)
        // NOTE: 핸들러 내에서 RequestToApi 재호출 시 200ms 지연 필요
        await Task.Delay(200);

        var message = new InterApiMessage
        {
            FromApiNid = "sender-api",
            Content = request.Query
        };
        var responsePacket = await sender.RequestToApi(request.TargetApiNid, message);
        var response = InterApiReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);

        var reply = new TriggerRequestToApiServerReply
        {
            Response = response.Response
        };
        sender.Reply(reply);
    }

    private Task HandleInterApiMessage(IPacket packet, IApiSender sender)
    {
        var request = InterApiMessage.Parser.ParseFrom(packet.Payload.DataSpan);

        var reply = new InterApiReply
        {
            Response = $"Processed: {request.Content} from {request.FromApiNid}"
        };
        sender.Reply(reply);
        return Task.CompletedTask;
    }

    private Task HandleBenchmarkApi(IPacket packet, IApiSender sender)
    {
        var request = BenchmarkApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 사전 할당된 페이로드 사용 (메모리 할당 오염 방지)
        var payload = PreallocatedPayloads.TryGetValue(request.ResponseSize, out var p)
            ? p
            : CreatePayload(request.ResponseSize);

        var reply = new BenchmarkApiReply
        {
            Sequence = request.Sequence,
            Payload = payload
        };

        sender.Reply(reply);
        return Task.CompletedTask;
    }

    private Task HandleTimerApi(IPacket packet, IApiSender sender)
    {
        var request = TimerApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var reply = new TimerApiReply
        {
            Content = $"Timer API Response: {request.Content}"
        };
        sender.Reply(reply);
        return Task.CompletedTask;
    }

    private Task HandleGetApiAccountId(IPacket packet, IApiSender sender)
    {
        var reply = new GetApiAccountIdReply
        {
            AccountId = sender.AccountId
        };
        sender.Reply(reply);
        return Task.CompletedTask;
    }

    private Task HandleApiSendToSystemPlay(IPacket packet, IApiSender sender)
    {
        var request = TriggerApiSendToSystemPlayRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var systemMsg = new SystemEchoRequest
        {
            Content = request.Message,
            FromServerId = "api-1"  // E2E test infrastructure uses fixed server IDs
        };

        sender.SendToSystem(request.TargetPlayNid, systemMsg);

        sender.Reply(new TriggerApiSendToSystemPlayReply { Success = true });
        return Task.CompletedTask;
    }

    private Task HandleApiSendToSystemApi(IPacket packet, IApiSender sender)
    {
        var request = TriggerApiSendToSystemApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var systemMsg = new SystemEchoRequest
        {
            Content = request.Message,
            FromServerId = "api-1"  // E2E test infrastructure uses fixed server IDs
        };

        sender.SendToSystem(request.TargetApiNid, systemMsg);

        sender.Reply(new TriggerApiSendToSystemApiReply { Success = true });
        return Task.CompletedTask;
    }

    private async Task HandleApiRequestToSystem(IPacket packet, IApiSender sender)
    {
        var request = TriggerApiRequestToSystemRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var systemMsg = new SystemEchoRequest
        {
            Content = request.Query,
            FromServerId = "api-1"  // E2E test infrastructure uses fixed server IDs
        };

        var response = await sender.RequestToSystem(request.TargetServerId, systemMsg);
        var systemReply = SystemEchoReply.Parser.ParseFrom(response.Payload.DataSpan);

        sender.Reply(new TriggerApiRequestToSystemReply
        {
            Response = systemReply.Content,
            HandledByServerId = systemReply.HandledByServerId
        });
    }
}
