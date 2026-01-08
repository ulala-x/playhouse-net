#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.SS.ApiServer;

public class BenchmarkApiController : IApiController
{
    public void Handles(IHandlerRegister register)
    {
        register.Add(nameof(CreateStageRequest), HandleCreateStage);
        register.Add(nameof(SSEchoRequest), HandleSSEchoRequest);
    }

    private async Task HandleCreateStage(IPacket packet, IApiSender sender)
    {
        var request = CreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        try {
            var result = await sender.CreateStage(request.PlayNid, request.StageType, request.StageId, CPacket.Empty("CreateStage"));
            sender.Reply(CPacket.Of(new CreateStageReply { Success = result.Result, StageId = request.StageId, PlayNid = request.PlayNid }));
        } catch (Exception ex) {
            sender.Reply(CPacket.Of(new CreateStageReply { Success = false, ErrorMessage = ex.Message }));
        }
    }

    private Task HandleSSEchoRequest(IPacket packet, IApiSender sender)
    {
        var request = SSEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var replyPacket = CPacket.Of(new SSEchoReply { Payload = request.Payload });

        // IsRequest 속성을 사용하여 응답 방식 결정
        if (sender.IsRequest)
        {
            sender.Reply(replyPacket);
        }
        else if (sender.StageId != 0)
        {
            sender.SendToStage(sender.SessionNid, sender.StageId, replyPacket);
        }

        return Task.CompletedTask;
    }
}
