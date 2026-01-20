#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Verification.Shared.Proto;

namespace PlayHouse.Verification.Shared.Infrastructure;

/// <summary>
/// DI 통합 검증용 Stage 구현 (Client Response Only).
/// IStageSender와 ITestService를 모두 DI로 주입받아 DI 통합을 검증합니다.
/// </summary>
/// <remarks>
/// Client Response Only 원칙:
/// - ❌ Static collections 금지
/// - ✅ DI로 주입받은 서비스를 사용하여 응답 생성
/// </remarks>
public class DITestStage : IStage
{
    private readonly ILogger<DITestStage> _logger;
    public IStageSender StageSender { get; }
    private readonly ITestService _testService;

    /// <summary>
    /// DI 컨테이너가 IStageSender, ITestService, ILogger를 모두 주입합니다.
    /// </summary>
    public DITestStage(IStageSender stageSender, ITestService testService, ILogger<DITestStage> logger)
    {
        StageSender = stageSender;
        _testService = testService;
        _logger = logger;
        _logger.LogDebug("DITestStage created for StageId={StageId}", stageSender.StageId);
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

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "EchoRequest":
                HandleEchoRequest(actor, packet);
                break;

            case "GetDIValueRequest":
                HandleGetDiValue(actor);
                break;

            default:
                actor.ActorSender.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }

        return Task.CompletedTask;
    }

    public Task OnDispatch(IPacket packet)
    {
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

        actor.ActorSender.Reply(CPacket.Of(echoReply));
    }

    private void HandleGetDiValue(IActor actor)
    {
        // DI로 주입받은 서비스의 값을 반환
        var reply = new GetDIValueReply
        {
            Value = _testService.GetValue()
        };

        actor.ActorSender.Reply(CPacket.Of(reply));
    }
}
