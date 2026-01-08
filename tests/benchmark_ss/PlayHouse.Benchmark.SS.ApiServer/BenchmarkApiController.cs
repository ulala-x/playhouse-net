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
        register.Add(nameof(SSEchoRequest), HandleSsEchoRequest);
        register.Add(nameof(SSEchoSend), HandleSsEchoSend);
    }

    private async Task HandleCreateStage(IPacket packet, IApiSender sender)
    {
        var request = CreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        try
        {
            var result = await sender.CreateStage(request.PlayNid, request.StageType, request.StageId, CPacket.Empty("CreateStage"));
            sender.Reply(CPacket.Of(new CreateStageReply { Success = result.Result, StageId = request.StageId, PlayNid = request.PlayNid }));
        }
        catch (Exception ex)
        {
            sender.Reply(CPacket.Of(new CreateStageReply { Success = false, ErrorMessage = ex.Message }));
        }
    }

    private Task HandleSsEchoRequest(IPacket packet, IApiSender sender)
    {
        var request = SSEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        // Standard Request -> Reply
        sender.Reply(CPacket.Of(new SSEchoReply { Payload = request.Payload }));
        return Task.CompletedTask;
    }

    private Task HandleSsEchoSend(IPacket packet, IApiSender sender)
    {
        var request = SSEchoSend.Parser.ParseFrom(packet.Payload.DataSpan);
        
        // Send-mode -> SendToStage (Using new public properties)
        if (sender.StageId != 0)
        {
            sender.SendToStage(sender.SessionNid, sender.StageId, CPacket.Of(new SSEchoReply { Payload = request.Payload }));
        }
        return Task.CompletedTask;
    }
}
