#nullable enable

using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Google.Protobuf;
using PlayHouse.Connector.Packet;
using PlayHouse.Connector.Protocol;
using Xunit;

namespace PlayHouse.Tests.Unit.Serialization;

/// <summary>
/// 패킷 직렬화/역직렬화 유닛 테스트.
/// Connector의 PacketEncoder/PacketDecoder 로직을 검증합니다.
/// 네트워크 호출 없이 메모리 내에서만 테스트합니다.
/// </summary>
public class PacketSerializationTests
{
    #region 1. PacketEncoder 테스트 (클라이언트 → 서버)

    [Fact(DisplayName = "빈 페이로드로 ClientPacket 인코딩")]
    public void PacketEncoder_EmptyPayload_EncodesCorrectly()
    {
        // Given
        var encoder = new PacketEncoder();
        var emptyMessage = new EmptyTestMessage();

        // When
        var encoded = encoder.EncodeWithLengthPrefix(emptyMessage, msgSeq: 1);

        // Then
        encoded.Should().NotBeNull();
        encoded.Length.Should().BeGreaterThan(4, "최소 4바이트 길이 헤더가 있어야 함");

        // 길이 헤더 검증 (little-endian)
        var packetSize = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0, 4));
        packetSize.Should().Be(encoded.Length - 4, "패킷 크기가 정확해야 함");
    }

    [Fact(DisplayName = "msgSeq=0인 일방향 메시지 인코딩")]
    public void PacketEncoder_OneWayMessage_HasZeroMsgSeq()
    {
        // Given
        var encoder = new PacketEncoder();
        var message = new EmptyTestMessage();

        // When
        var encoded = encoder.EncodeMessage(message);

        // Then
        encoded.Should().NotBeNull();

        // MsgSeq는 기본값 0
        // Message Descriptor Name이 MsgId로 사용됨
    }

    [Fact(DisplayName = "요청 메시지에 MsgSeq가 포함됨")]
    public void PacketEncoder_RequestMessage_IncludesMsgSeq()
    {
        // Given
        var encoder = new PacketEncoder();
        var message = new EmptyTestMessage();
        ushort expectedSeq = 12345;

        // When
        var encoded = encoder.EncodeMessage(message, msgSeq: expectedSeq);

        // Then
        encoded.Should().NotBeNull();
        // MsgSeq가 인코딩에 포함됨
    }

    [Fact(DisplayName = "null 메시지 인코딩 시 ArgumentNullException")]
    public void PacketEncoder_NullMessage_ThrowsArgumentNullException()
    {
        // Given
        var encoder = new PacketEncoder();

        // When & Then
        var act = () => encoder.EncodeMessage<EmptyTestMessage>(null!, msgSeq: 1);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region 2. PacketDecoder 테스트 (서버 → 클라이언트)

    [Fact(DisplayName = "유효한 ServerPacket 디코딩")]
    public void PacketDecoder_ValidServerPacket_DecodesCorrectly()
    {
        // Given
        var decoder = new PacketDecoder();
        var serverPacket = new ServerPacket
        {
            MsgSeq = 100,
            MsgId = "TestMessage",
            ErrorCode = 0,
            Payload = ByteString.CopyFromUtf8("test payload")
        };

        var packetBytes = SerializePacket(serverPacket);
        var encodedData = CreateLengthPrefixedPacket(packetBytes);

        // When
        var results = decoder.ProcessData(encodedData).ToList();

        // Then
        results.Should().HaveCount(1);
        results[0].MsgSeq.Should().Be(100);
        results[0].MsgId.Should().Be("TestMessage");
        results[0].ErrorCode.Should().Be(0);
        results[0].Payload.ToStringUtf8().Should().Be("test payload");
    }

    [Fact(DisplayName = "분할된 패킷 수신 시 올바르게 조립")]
    public void PacketDecoder_FragmentedData_AssemblesCorrectly()
    {
        // Given
        var decoder = new PacketDecoder();
        var serverPacket = new ServerPacket
        {
            MsgSeq = 1,
            MsgId = "FragmentedTest",
            ErrorCode = 0,
            Payload = ByteString.CopyFromUtf8("fragmented test")
        };

        var packetBytes = SerializePacket(serverPacket);
        var fullData = CreateLengthPrefixedPacket(packetBytes);

        // When - 데이터를 여러 조각으로 나눠서 전송
        var part1 = fullData[..3]; // 길이 헤더 일부
        var part2 = fullData[3..10]; // 길이 헤더 나머지 + 데이터 일부
        var part3 = fullData[10..]; // 나머지 데이터

        var results1 = decoder.ProcessData(part1).ToList();
        var results2 = decoder.ProcessData(part2).ToList();
        var results3 = decoder.ProcessData(part3).ToList();

        // Then
        results1.Should().BeEmpty("아직 완전한 패킷이 아님");
        results2.Should().BeEmpty("아직 완전한 패킷이 아님");
        results3.Should().HaveCount(1, "마지막 조각으로 패킷 완성");
        results3[0].Payload.ToStringUtf8().Should().Be("fragmented test");
    }

    [Fact(DisplayName = "연속된 여러 패킷 디코딩")]
    public void PacketDecoder_MultiplePackets_DecodesAll()
    {
        // Given
        var decoder = new PacketDecoder();

        var packet1 = new ServerPacket { MsgSeq = 1, MsgId = "Msg1", ErrorCode = 0 };
        var packet2 = new ServerPacket { MsgSeq = 2, MsgId = "Msg2", ErrorCode = 0 };
        var packet3 = new ServerPacket { MsgSeq = 3, MsgId = "Msg3", ErrorCode = 0 };

        var data1 = CreateLengthPrefixedPacket(SerializePacket(packet1));
        var data2 = CreateLengthPrefixedPacket(SerializePacket(packet2));
        var data3 = CreateLengthPrefixedPacket(SerializePacket(packet3));

        // 모든 패킷을 한 번에 전송
        var combinedData = data1.Concat(data2).Concat(data3).ToArray();

        // When
        var results = decoder.ProcessData(combinedData).ToList();

        // Then
        results.Should().HaveCount(3);
        results[0].MsgSeq.Should().Be(1);
        results[1].MsgSeq.Should().Be(2);
        results[2].MsgSeq.Should().Be(3);
    }

    [Fact(DisplayName = "빈 데이터 처리 시 빈 결과")]
    public void PacketDecoder_EmptyData_ReturnsEmpty()
    {
        // Given
        var decoder = new PacketDecoder();

        // When
        var results1 = decoder.ProcessData(null!).ToList();
        var results2 = decoder.ProcessData(Array.Empty<byte>()).ToList();

        // Then
        results1.Should().BeEmpty();
        results2.Should().BeEmpty();
    }

    [Fact(DisplayName = "잘못된 패킷 크기 시 예외 발생")]
    public void PacketDecoder_InvalidPacketSize_ThrowsException()
    {
        // Given
        var decoder = new PacketDecoder();

        // 음수 크기를 나타내는 데이터 (0xFF로 시작하면 매우 큰 값)
        var invalidData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

        // When & Then
        var act = () => decoder.ProcessData(invalidData).ToList();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid packet size*");
    }

    [Fact(DisplayName = "Reset 후 버퍼가 비워짐")]
    public void PacketDecoder_Reset_ClearsBuffer()
    {
        // Given
        var decoder = new PacketDecoder();
        var partialData = new byte[] { 0, 0, 0, 100 }; // 100바이트 패킷의 길이 헤더만

        decoder.ProcessData(partialData).ToList(); // 버퍼에 데이터 추가

        // When
        decoder.Reset();

        // 새로운 완전한 패킷 전송
        var newPacket = new ServerPacket { MsgSeq = 999, MsgId = "ResetTest" };
        var newData = CreateLengthPrefixedPacket(SerializePacket(newPacket));
        var results = decoder.ProcessData(newData).ToList();

        // Then
        results.Should().HaveCount(1);
        results[0].MsgSeq.Should().Be(999, "새 패킷이 올바르게 디코딩되어야 함");
    }

    #endregion

    #region 3. 라운드트립 테스트 (인코딩 → 디코딩)

    [Fact(DisplayName = "ServerPacket 직렬화 라운드트립")]
    public void ServerPacket_RoundTrip_PreservesData()
    {
        // Given
        var original = new ServerPacket
        {
            MsgSeq = 11111,
            MsgId = "RoundTripTest",
            ErrorCode = 500,
            Payload = ByteString.CopyFromUtf8("server response data")
        };

        // When
        var serialized = SerializePacket(original);
        var deserialized = ServerPacket.Parser.ParseFrom(serialized);

        // Then
        deserialized.MsgSeq.Should().Be(original.MsgSeq);
        deserialized.MsgId.Should().Be(original.MsgId);
        deserialized.ErrorCode.Should().Be(original.ErrorCode);
        deserialized.Payload.Should().Equal(original.Payload);
    }

    [Fact(DisplayName = "Encoder와 Decoder 간 연동")]
    public void PacketEncoder_Decoder_Integration()
    {
        // Given
        var encoder = new PacketEncoder();
        var decoder = new PacketDecoder();
        var testMessage = new EmptyTestMessage();

        // 클라이언트에서 인코딩
        var clientEncoded = encoder.EncodeWithLengthPrefix(testMessage, msgSeq: 777);

        // 서버에서 응답 생성 (ServerPacket 형식)
        var serverResponse = new ServerPacket
        {
            MsgSeq = 777, // 같은 시퀀스로 응답
            MsgId = "ResponseMessage", // 응답 메시지 ID
            ErrorCode = 0,
            Payload = ByteString.CopyFromUtf8("response")
        };

        var serverEncoded = CreateLengthPrefixedPacket(SerializePacket(serverResponse));

        // When - 클라이언트에서 서버 응답 디코딩
        var decodedResponses = decoder.ProcessData(serverEncoded).ToList();

        // Then
        decodedResponses.Should().HaveCount(1);
        decodedResponses[0].MsgSeq.Should().Be(777);
        decodedResponses[0].MsgId.Should().Be("ResponseMessage");
        decodedResponses[0].ErrorCode.Should().Be(0);
    }

    #endregion

    #region 4. 에지 케이스 테스트

    [Fact(DisplayName = "최대 ushort 값 처리")]
    public void Packet_MaxUShortValues_HandledCorrectly()
    {
        // Given
        var packet = new ServerPacket
        {
            MsgSeq = ushort.MaxValue,
            MsgId = "MaxValueTest",
            ErrorCode = ushort.MaxValue
        };

        // When
        var encoded = CreateLengthPrefixedPacket(SerializePacket(packet));
        var decoder = new PacketDecoder();
        var results = decoder.ProcessData(encoded).ToList();

        // Then
        results.Should().HaveCount(1);
        results[0].MsgSeq.Should().Be(ushort.MaxValue);
        results[0].MsgId.Should().Be("MaxValueTest");
        results[0].ErrorCode.Should().Be(ushort.MaxValue);
    }

    [Fact(DisplayName = "대용량 페이로드 처리")]
    public void Packet_LargePayload_HandledCorrectly()
    {
        // Given
        var largePayload = new byte[100_000]; // 100KB
        new Random(42).NextBytes(largePayload);

        var packet = new ServerPacket
        {
            MsgSeq = 1,
            MsgId = "LargePayload",
            Payload = ByteString.CopyFrom(largePayload)
        };

        // When
        var encoded = CreateLengthPrefixedPacket(SerializePacket(packet));
        var decoder = new PacketDecoder();
        var results = decoder.ProcessData(encoded).ToList();

        // Then
        results.Should().HaveCount(1);
        results[0].Payload.ToByteArray().Should().Equal(largePayload);
    }

    [Fact(DisplayName = "연속된 작은 패킷 대량 처리")]
    public void Packet_ManySmallPackets_ProcessedEfficiently()
    {
        // Given
        var decoder = new PacketDecoder();
        var allData = new List<byte>();
        const int packetCount = 1000;

        for (int i = 0; i < packetCount; i++)
        {
            var packet = new ServerPacket
            {
                MsgSeq = (ushort)(i % ushort.MaxValue),
                MsgId = $"Msg{i}"
            };
            allData.AddRange(CreateLengthPrefixedPacket(SerializePacket(packet)));
        }

        // When
        var results = decoder.ProcessData(allData.ToArray()).ToList();

        // Then
        results.Should().HaveCount(packetCount);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// IMessage를 바이트 배열로 직렬화합니다.
    /// </summary>
    private static byte[] SerializePacket(IMessage message)
    {
        using var ms = new MemoryStream();
        using var cos = new CodedOutputStream(ms);
        message.WriteTo(cos);
        cos.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// 패킷 데이터에 4바이트 big-endian 길이 접두사를 추가합니다.
    /// </summary>
    private static byte[] CreateLengthPrefixedPacket(byte[] packetData)
    {
        var result = new byte[packetData.Length + 4];
        var size = packetData.Length;

        // Big-endian 길이 헤더
        result[0] = (byte)(size >> 24);
        result[1] = (byte)(size >> 16);
        result[2] = (byte)(size >> 8);
        result[3] = (byte)size;

        Array.Copy(packetData, 0, result, 4, packetData.Length);
        return result;
    }

    #endregion
}

/// <summary>
/// 테스트용 빈 Protobuf 메시지.
/// </summary>
public sealed class EmptyTestMessage : IMessage<EmptyTestMessage>
{
    private static readonly Lazy<Google.Protobuf.Reflection.MessageDescriptor> _descriptor =
        new Lazy<Google.Protobuf.Reflection.MessageDescriptor>(() =>
        {
            // Proto 파일 정의: message EmptyTestMessage {}
            var descriptorData = Convert.FromBase64String(
                "ChpFbXB0eVRlc3RNZXNzYWdlLnByb3RvIhIKEEVtcHR5VGVzdE1lc3NhZ2Vi" +
                "BnByb3RvMw==");
            var fileDescriptor = Google.Protobuf.Reflection.FileDescriptor.FromGeneratedCode(
                descriptorData,
                new Google.Protobuf.Reflection.FileDescriptor[] { },
                new Google.Protobuf.Reflection.GeneratedClrTypeInfo(
                    null,
                    null,
                    new Google.Protobuf.Reflection.GeneratedClrTypeInfo[] {
                        new Google.Protobuf.Reflection.GeneratedClrTypeInfo(
                            typeof(EmptyTestMessage),
                            EmptyTestMessage.Parser,
                            new string[] { },
                            null,
                            null,
                            null,
                            null)
                    }));
            return fileDescriptor.MessageTypes[0];
        });

    public static MessageParser<EmptyTestMessage> Parser { get; } =
        new MessageParser<EmptyTestMessage>(() => new EmptyTestMessage());

    public Google.Protobuf.Reflection.MessageDescriptor Descriptor => _descriptor.Value;

    public EmptyTestMessage Clone() => new EmptyTestMessage();

    public void MergeFrom(EmptyTestMessage message) { }

    public void MergeFrom(CodedInputStream input)
    {
        while (input.ReadTag() != 0) { }
    }

    public void WriteTo(CodedOutputStream output) { }

    public int CalculateSize() => 0;

    public bool Equals(EmptyTestMessage? other) => other != null;

    public override bool Equals(object? obj) => obj is EmptyTestMessage;

    public override int GetHashCode() => 0;

    public override string ToString() => "EmptyTestMessage";
}
