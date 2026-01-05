#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.SS.ApiServer;

/// <summary>
/// Server-to-Server Echo 벤치마크용 API Controller.
/// </summary>
public class BenchmarkApiController : IApiController
{
    private IApiSender? _apiSender;

    public void Handles(IHandlerRegister register)
    {
        register.Add(nameof(CreateStageRequest), HandleCreateStage);
        register.Add(nameof(SSEchoRequest), HandleSSEchoRequest);
    }

    /// <summary>
    /// Stage 생성 요청 처리.
    /// HTTP 클라이언트에서 온 CreateStageRequest를 받아서 PlayServer에 Stage를 생성합니다.
    /// </summary>
    private async Task HandleCreateStage(IPacket packet, IApiSender sender)
    {
        _apiSender = sender;

        var request = CreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        try
        {
            var createPacket = CPacket.Empty("CreateStage");
            var result = await sender.CreateStage(
                request.PlayNid,
                request.StageType,
                request.StageId,
                createPacket);

            var reply = new CreateStageReply
            {
                Success = result.Result,
                ErrorCode = result.Result ? 0 : -1,
                StageId = request.StageId,
                PlayNid = request.PlayNid
            };

            sender.Reply(CPacket.Of(reply));
        }
        catch (Exception ex)
        {
            var reply = new CreateStageReply
            {
                Success = false,
                ErrorCode = -1,
                StageId = request.StageId,
                PlayNid = request.PlayNid,
                ErrorMessage = ex.Message
            };

            sender.Reply(CPacket.Of(reply));
        }
    }

    /// <summary>
    /// Echo 요청 처리.
    /// SSEchoRequest를 받아서 동일한 Payload를 담은 SSEchoReply를 반환합니다.
    /// </summary>
    private Task HandleSSEchoRequest(IPacket packet, IApiSender sender)
    {
        _apiSender = sender;

        var request = SSEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 그대로 Echo 응답
        var reply = new SSEchoReply
        {
            Payload = request.Payload
        };

        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }
}
