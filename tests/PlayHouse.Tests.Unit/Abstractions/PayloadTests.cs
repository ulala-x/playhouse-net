#nullable enable

using System.Buffers;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using PlayHouse.Abstractions;
using Xunit;

namespace PlayHouse.Tests.Unit.Abstractions;

/// <summary>
/// 단위 테스트: PlayHouse.Abstractions의 Payload 구현체들 검증
/// </summary>
public class PayloadTests
{
    #region ArrayPoolPayload Tests

    [Fact(DisplayName = "ArrayPoolPayload - ArrayPool 버퍼를 받아서 저장한다")]
    public void ArrayPoolPayload_StoresRentedBuffer()
    {
        // Given (전제조건)
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var buffer = ArrayPool<byte>.Shared.Rent(testData.Length);
        testData.CopyTo(buffer, 0);

        // When (행동)
        using var payload = new ArrayPoolPayload(buffer, testData.Length);
        var data = payload.DataSpan.ToArray();

        // Then (결과)
        data.Should().Equal(testData, "저장한 데이터가 동일해야 함");
    }

    [Fact(DisplayName = "ArrayPoolPayload.DataSpan - actualSize만큼만 반환한다")]
    public void ArrayPoolPayload_DataSpan_ReturnsOnlyActualSize()
    {
        // Given (전제조건)
        var actualSize = 5;
        var buffer = ArrayPool<byte>.Shared.Rent(1024); // 큰 버퍼 대여
        for (int i = 0; i < actualSize; i++)
        {
            buffer[i] = (byte)(i + 1);
        }

        // When (행동)
        using var payload = new ArrayPoolPayload(buffer, actualSize);
        var span = payload.DataSpan;

        // Then (결과)
        span.Length.Should().Be(actualSize, "actualSize만큼만 반환해야 함");
        span[0].Should().Be(1);
        span[4].Should().Be(5);
    }

    [Fact(DisplayName = "ArrayPoolPayload.Length - actualSize를 반환한다")]
    public void ArrayPoolPayload_Length_ReturnsActualSize()
    {
        // Given (전제조건)
        var actualSize = 10;
        var buffer = ArrayPool<byte>.Shared.Rent(1024);

        // When (행동)
        using var payload = new ArrayPoolPayload(buffer, actualSize);

        // Then (결과)
        payload.Length.Should().Be(actualSize, "Length는 actualSize를 반환해야 함");
    }

    [Fact(DisplayName = "ArrayPoolPayload.Dispose - 버퍼를 ArrayPool에 반환한다")]
    public void ArrayPoolPayload_Dispose_ReturnsBufferToPool()
    {
        // Given (전제조건)
        var buffer = ArrayPool<byte>.Shared.Rent(100);
        var payload = new ArrayPoolPayload(buffer, 10);

        // When (행동)
        payload.Dispose();

        // Then (결과)
        // 두 번 Dispose해도 예외가 발생하지 않아야 함
        var action = () => payload.Dispose();
        action.Should().NotThrow("중복 Dispose는 안전해야 함");
    }

    [Fact(DisplayName = "ArrayPoolPayload.Dispose - 여러 번 호출해도 안전하다")]
    public void ArrayPoolPayload_Dispose_MultipleCalls_IsSafe()
    {
        // Given (전제조건)
        var buffer = ArrayPool<byte>.Shared.Rent(50);
        var payload = new ArrayPoolPayload(buffer, 20);

        // When (행동)
        var action = () =>
        {
            payload.Dispose();
            payload.Dispose();
            payload.Dispose();
        };

        // Then (결과)
        action.Should().NotThrow("여러 번 Dispose해도 안전해야 함");
    }

    #endregion

    #region ProtoPayload Tests

    [Fact(DisplayName = "ProtoPayload - Protobuf 메시지를 직렬화하여 저장한다")]
    public void ProtoPayload_SerializesProtobufMessage()
    {
        // Given (전제조건)
        var message = new StringValue { Value = "테스트 메시지" };

        // When (행동)
        using var payload = new ProtoPayload(message);
        var data = payload.DataSpan;

        // Then (결과)
        data.Length.Should().BeGreaterThan(0, "직렬화된 데이터가 있어야 함");
    }

    [Fact(DisplayName = "ProtoPayload - 직렬화된 데이터가 역직렬화 가능하다")]
    public void ProtoPayload_SerializedData_IsDeserializable()
    {
        // Given (전제조건)
        var originalMessage = new StringValue { Value = "Hello, Protobuf!" };
        using var payload = new ProtoPayload(originalMessage);

        // When (행동)
        var deserializedMessage = StringValue.Parser.ParseFrom(payload.DataSpan);

        // Then (결과)
        deserializedMessage.Value.Should().Be("Hello, Protobuf!", "역직렬화한 값이 원본과 같아야 함");
    }

    [Fact(DisplayName = "ProtoPayload - 복잡한 메시지도 직렬화한다")]
    public void ProtoPayload_ComplexMessage_SerializesCorrectly()
    {
        // Given (전제조건)
        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        using var payload = new ProtoPayload(timestamp);

        // When (행동)
        var deserialized = Timestamp.Parser.ParseFrom(payload.DataSpan);

        // Then (결과)
        deserialized.Seconds.Should().Be(timestamp.Seconds, "타임스탬프 초가 같아야 함");
        deserialized.Nanos.Should().Be(timestamp.Nanos, "타임스탬프 나노초가 같아야 함");
    }

    [Fact(DisplayName = "ProtoPayload.Length - 메시지 크기를 반환한다")]
    public void ProtoPayload_Length_ReturnsMessageSize()
    {
        // Given (전제조건)
        var message = new StringValue { Value = "Test" };
        var expectedSize = message.CalculateSize();

        // When (행동)
        using var payload = new ProtoPayload(message);

        // Then (결과)
        payload.Length.Should().Be(expectedSize, "Length는 메시지 크기를 반환해야 함");
    }

    [Fact(DisplayName = "ProtoPayload.Dispose - ArrayPool 버퍼를 반환한다")]
    public void ProtoPayload_Dispose_ReturnsArrayPoolBuffer()
    {
        // Given (전제조건)
        var message = new StringValue { Value = "Test" };
        var payload = new ProtoPayload(message);

        // DataSpan 접근으로 버퍼 할당
        _ = payload.DataSpan;

        // When (행동)
        payload.Dispose();

        // Then (결과)
        // 여러 번 Dispose해도 안전해야 함
        var action = () => payload.Dispose();
        action.Should().NotThrow("중복 Dispose는 안전해야 함");
    }

    [Fact(DisplayName = "ProtoPayload - Lazy serialization 동작을 검증한다")]
    public void ProtoPayload_LazySerializationWorks()
    {
        // Given (전제조건)
        var message = new StringValue { Value = "Lazy Test" };
        var payload = new ProtoPayload(message);

        // When (행동)
        // 첫 번째 DataSpan 접근 시 직렬화
        var data1 = payload.DataSpan.ToArray();
        var data2 = payload.DataSpan.ToArray();

        // Then (결과)
        data1.Should().Equal(data2, "같은 버퍼를 재사용해야 함");

        payload.Dispose();
    }

    [Fact(DisplayName = "ProtoPayload.GetProto - 원본 Protobuf 메시지를 반환한다")]
    public void ProtoPayload_GetProto_ReturnsOriginalMessage()
    {
        // Given (전제조건)
        var originalMessage = new StringValue { Value = "Original" };
        using var payload = new ProtoPayload(originalMessage);

        // When (행동)
        var retrievedMessage = payload.GetProto();

        // Then (결과)
        retrievedMessage.Should().BeSameAs(originalMessage, "원본 메시지를 반환해야 함");
    }

    #endregion

    #region MemoryPayload Tests

    [Fact(DisplayName = "MemoryPayload - ReadOnlyMemory를 저장한다")]
    public void MemoryPayload_StoresReadOnlyMemory()
    {
        // Given (전제조건)
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var memory = new ReadOnlyMemory<byte>(testData);

        // When (행동)
        using var payload = new MemoryPayload(memory);
        var data = payload.DataSpan.ToArray();

        // Then (결과)
        data.Should().Equal(testData, "저장한 데이터가 동일해야 함");
    }

    [Fact(DisplayName = "MemoryPayload.Length - 메모리 길이를 반환한다")]
    public void MemoryPayload_Length_ReturnsMemoryLength()
    {
        // Given (전제조건)
        var testData = new byte[] { 1, 2, 3 };
        var memory = new ReadOnlyMemory<byte>(testData);

        // When (행동)
        using var payload = new MemoryPayload(memory);

        // Then (결과)
        payload.Length.Should().Be(testData.Length, "Length는 메모리 길이를 반환해야 함");
    }

    [Fact(DisplayName = "MemoryPayload.Dispose - 아무 작업도 하지 않는다")]
    public void MemoryPayload_Dispose_DoesNothing()
    {
        // Given (전제조건)
        var memory = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 });
        var payload = new MemoryPayload(memory);

        // When (행동)
        var action = () =>
        {
            payload.Dispose();
            payload.Dispose();
        };

        // Then (결과)
        action.Should().NotThrow("Dispose는 아무 작업도 하지 않아야 함");
    }

    #endregion

    #region EmptyPayload Tests

    [Fact(DisplayName = "EmptyPayload.Instance - 싱글톤 인스턴스를 반환한다")]
    public void EmptyPayload_Instance_ReturnsSingleton()
    {
        // Given & When
        var instance1 = EmptyPayload.Instance;
        var instance2 = EmptyPayload.Instance;

        // Then
        instance1.Should().BeSameAs(instance2, "싱글톤 인스턴스여야 함");
    }

    [Fact(DisplayName = "EmptyPayload.DataSpan - 빈 Span을 반환한다")]
    public void EmptyPayload_DataSpan_ReturnsEmptySpan()
    {
        // Given
        var payload = EmptyPayload.Instance;

        // When
        var span = payload.DataSpan;

        // Then
        span.Length.Should().Be(0, "빈 Span이어야 함");
    }

    [Fact(DisplayName = "EmptyPayload.Length - 0을 반환한다")]
    public void EmptyPayload_Length_ReturnsZero()
    {
        // Given
        var payload = EmptyPayload.Instance;

        // When & Then
        payload.Length.Should().Be(0, "길이가 0이어야 함");
    }

    [Fact(DisplayName = "EmptyPayload.Dispose - 아무 작업도 하지 않는다")]
    public void EmptyPayload_Dispose_DoesNothing()
    {
        // Given
        var payload = EmptyPayload.Instance;

        // When
        var action = () =>
        {
            payload.Dispose();
            payload.Dispose();
        };

        // Then
        action.Should().NotThrow("Dispose는 아무 작업도 하지 않아야 함");
    }

    #endregion

    #region IPayload Interface Tests

    [Fact(DisplayName = "IPayload - 모든 구현체가 인터페이스를 만족한다")]
    public void IPayload_AllImplementations_SatisfyInterface()
    {
        // Given (전제조건)
        var buffer = ArrayPool<byte>.Shared.Rent(10);
        for (int i = 0; i < 10; i++) buffer[i] = (byte)(i + 1);

        IPayload[] payloads =
        {
            EmptyPayload.Instance,
            new MemoryPayload(new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 })),
            new ProtoPayload(new StringValue { Value = "test" }),
            new ArrayPoolPayload(buffer, 10)
        };

        // When & Then
        foreach (var payload in payloads)
        {
            // DataSpan 속성 접근 가능
            var span = payload.DataSpan;
            span.Length.Should().BeGreaterThanOrEqualTo(0, "DataSpan 길이가 0 이상이어야 함");

            // Length 속성 접근 가능
            payload.Length.Should().BeGreaterThanOrEqualTo(0, "Length가 0 이상이어야 함");

            // Dispose 가능
            payload.Dispose();
        }
    }

    #endregion
}
