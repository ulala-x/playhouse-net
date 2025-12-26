#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.SS.ApiServer;

/// <summary>
/// Server-to-Server 벤치마크용 API Controller.
/// SSBenchmarkRequest를 받아서 지정된 크기의 SSBenchmarkReply를 반환합니다.
/// </summary>
public class BenchmarkApiController : IApiController
{
    /// <summary>
    /// 페이로드 캐시 - 크기별로 한 번만 생성하여 재사용
    /// </summary>
    private static readonly ConcurrentDictionary<int, Google.Protobuf.ByteString> PayloadCache = new();

    private static Google.Protobuf.ByteString GetOrCreatePayload(int size)
    {
        if (size <= 0) return Google.Protobuf.ByteString.Empty;

        return PayloadCache.GetOrAdd(size, s =>
        {
            var payload = new byte[s];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i % 256);
            }
            return Google.Protobuf.ByteString.CopyFrom(payload);
        });
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add(nameof(SSBenchmarkRequest), HandleSSBenchmark);
    }

    /// <summary>
    /// Server-to-Server 벤치마크 요청 처리.
    /// Stage나 다른 API Server에서 온 SSBenchmarkRequest를 처리하여
    /// 지정된 크기의 페이로드를 담은 SSBenchmarkReply를 반환합니다.
    /// </summary>
    private Task HandleSSBenchmark(IPacket packet, IApiSender sender)
    {
        var request = SSBenchmarkRequest.Parser.ParseFrom(packet.Payload.Data.Span);

        // 캐시된 페이로드 사용 (첫 번째 요청 시 생성, 이후 재사용)
        var payload = GetOrCreatePayload(request.ResponseSize);

        var reply = new SSBenchmarkReply
        {
            Sequence = request.Sequence,
            Payload = payload
        };

        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }
}
