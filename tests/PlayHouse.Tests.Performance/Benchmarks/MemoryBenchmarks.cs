using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Google.Protobuf;
using PlayHouse.Connector.Protocol;
using PlayHouse.Runtime.Proto;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Tests.E2E.Proto;

namespace PlayHouse.Tests.Performance.Benchmarks;

/// <summary>
/// 메모리 할당량 측정.
/// GC 압박 분석을 위한 벤치마크.
/// </summary>
[MemoryDiagnoser]
[GcForce]
public class MemoryBenchmarks
{
    private EchoRequest _echoRequest = null!;
    private RouteHeader _routeHeader = null!;
    private byte[] _payloadBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _echoRequest = new EchoRequest { Content = "test-content-for-benchmark", Sequence = 12345 };
        _routeHeader = new RouteHeader
        {
            MsgSeq = 1,
            ServiceId = 1,
            MsgId = "EchoRequest",
            From = "server-1",
            StageId = 1001,
            AccountId = 123456
        };
        _payloadBytes = _echoRequest.ToByteArray();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // 각 반복마다 GC 실행하여 메모리 할당 측정 정확도 향상
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// 클라이언트 Packet 생성-해제 메모리 할당
    /// </summary>
    [Benchmark(Baseline = true, Description = "Client Packet create/dispose")]
    public void ClientPacket_CreateDispose()
    {
        using var packet = new Packet(_echoRequest);
        _ = packet.MsgId;
    }

    /// <summary>
    /// RuntimeRoutePacket 생성-해제 메모리 할당 (byte[] 팩토리)
    /// </summary>
    [Benchmark(Description = "RuntimeRoutePacket (byte[]) create/dispose")]
    public void RuntimeRoutePacket_ByteArray_CreateDispose()
    {
        using var packet = RuntimeRoutePacket.Of(_routeHeader, _payloadBytes);
        _ = packet.MsgId;
    }

    /// <summary>
    /// RuntimeRoutePacket 생성-해제 메모리 할당 (ByteString 팩토리)
    /// </summary>
    [Benchmark(Description = "RuntimeRoutePacket (ByteString) create/dispose")]
    public void RuntimeRoutePacket_ByteString_CreateDispose()
    {
        var byteString = ByteString.CopyFrom(_payloadBytes);
        using var packet = RuntimeRoutePacket.Of(_routeHeader, byteString);
        _ = packet.MsgId;
    }

    /// <summary>
    /// RuntimeRoutePacket.GetPayloadBytes() 호출 시 메모리 할당
    /// 이것이 최적화 대상 (ToArray() 호출)
    /// </summary>
    [Benchmark(Description = "GetPayloadBytes() allocation")]
    public byte[] GetPayloadBytes_Allocation()
    {
        using var packet = RuntimeRoutePacket.Of(_routeHeader, _payloadBytes);
        return packet.GetPayloadBytes();  // ToArray() 호출됨
    }

    /// <summary>
    /// RouteHeader 직렬화 메모리 할당
    /// </summary>
    [Benchmark(Description = "RouteHeader serialize")]
    public byte[] RouteHeader_Serialize()
    {
        return _routeHeader.ToByteArray();
    }

    /// <summary>
    /// Protobuf 메시지 직렬화 메모리 할당
    /// </summary>
    [Benchmark(Description = "Protobuf message serialize")]
    public byte[] ProtobufMessage_Serialize()
    {
        return _echoRequest.ToByteArray();
    }
}
