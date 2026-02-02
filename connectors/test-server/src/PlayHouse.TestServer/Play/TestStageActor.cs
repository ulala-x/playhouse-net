#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Extensions.Proto;
using PlayHouse.TestServer.Proto;

namespace PlayHouse.TestServer.Play;

/// <summary>
/// Test Server용 Stage 구현.
/// 클라이언트 커넥터 E2E 테스트를 위한 응답 생성 핸들러.
/// </summary>
public class TestStageActor : IStage
{
    private readonly ILogger<TestStageActor> _logger;

    public IStageLink StageLink { get; }

    public TestStageActor(IStageLink stageLink, ILogger<TestStageActor>? logger = null)
    {
        StageLink = stageLink;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TestStageActor>.Instance;
        _logger.LogDebug("TestStageActor created for StageId={StageId}", stageLink.StageId);
    }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        // Stage 생성 성공 응답
        var reply = new CreateStageReply
        {
            ReceivedStageName = "TestStage",
            ReceivedMaxPlayers = 100,
            Created = true
        };

        return Task.FromResult<(bool, IPacket)>((true, ProtoCPacketExtensions.OfProto(reply)));
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
    public Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "EchoRequest":
                HandleEchoRequest(actor, packet);
                break;

            case "NoResponseRequest":
                // 의도적으로 응답하지 않음 (타임아웃 테스트용)
                break;

            case "FailRequest":
                HandleFailRequest(actor, packet);
                break;

            case "LargePayloadRequest":
                HandleLargePayloadRequest(actor, packet);
                break;

            case "BroadcastRequest":
                HandleBroadcastRequest(actor, packet);
                break;

            default:
                // 알 수 없는 메시지 처리
                actor.ActorLink.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 서버 간 메시지 수신 시 처리.
    /// </summary>
    public Task OnDispatch(IPacket packet)
    {
        // Stage간 메시지 처리 (필요시 확장)
        return Task.CompletedTask;
    }

    private void HandleEchoRequest(IActor actor, IPacket packet)
    {
        var echoRequest = EchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var echoReply = new EchoReply
        {
            Content = echoRequest.Content,
            Sequence = echoRequest.Sequence,
            ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        actor.ActorLink.Reply(echoReply);
    }

    private void HandleLargePayloadRequest(IActor actor, IPacket packet)
    {
        // 1MB (1,048,576 bytes) 페이로드 생성
        var payload = new byte[1048576];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        var reply = new BenchmarkReply
        {
            Sequence = 1,
            ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Payload = Google.Protobuf.ByteString.CopyFrom(payload)
        };

        actor.ActorLink.Reply(reply);
    }

    private void HandleFailRequest(IActor actor, IPacket packet)
    {
        var failRequest = FailRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var failReply = new FailReply
        {
            ErrorCode = failRequest.ErrorCode,
            Message = failRequest.ErrorMessage
        };

        actor.ActorLink.Reply(failReply);
    }

    private void HandleBroadcastRequest(IActor actor, IPacket packet)
    {
        var broadcastRequest = BroadcastRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // Stage에 있는 모든 Actor에게 BroadcastNotify 전송
        var broadcastNotify = new BroadcastNotify
        {
            EventType = "TestBroadcast",
            Data = broadcastRequest.Content,
            FromAccountId = long.Parse(actor.ActorLink.AccountId)
        };

        // 모든 Actor에게 Push (간단한 구현: 요청한 Actor에게만 전송)
        actor.ActorLink.SendToClient(broadcastNotify);
    }
}
