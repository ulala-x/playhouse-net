#nullable enable

using System;
using FluentAssertions;
using PlayHouse.Connector.Infrastructure.Buffers;
using Xunit;

namespace PlayHouse.Tests.Unit.Connector.Infrastructure.Buffers;

/// <summary>
/// 단위 테스트: RingBuffer의 Write/Read, 순환 동작, 용량 초과, Clear 검증
/// </summary>
public class RingBufferTests
{
    #region 기본 Write/Read 테스트

    [Fact(DisplayName = "RingBuffer - 기본 Write/Read 동작")]
    public void RingBuffer_BasicWriteRead_Works()
    {
        // Given
        using var buffer = new RingBuffer(64);
        var testData = new byte[] { 1, 2, 3, 4, 5 };

        // When
        buffer.WriteBytes(testData);

        // Then
        buffer.Count.Should().Be(5, "5바이트가 쓰여졌어야 함");
        buffer.FreeSpace.Should().Be(59, "64 - 5 = 59바이트 남음");

        var readSpan = buffer.Peek(5);
        readSpan.ToArray().Should().Equal(testData, "쓴 데이터와 읽은 데이터가 같아야 함");
    }

    [Fact(DisplayName = "RingBuffer - Peek는 데이터를 소비하지 않는다")]
    public void RingBuffer_Peek_DoesNotConsumeData()
    {
        // Given
        using var buffer = new RingBuffer(64);
        var testData = new byte[] { 10, 20, 30 };
        buffer.WriteBytes(testData);

        // When
        var peek1 = buffer.Peek(3);
        var peek2 = buffer.Peek(3);

        // Then
        peek1.ToArray().Should().Equal(testData, "첫 번째 Peek 결과");
        peek2.ToArray().Should().Equal(testData, "두 번째 Peek 결과도 동일해야 함");
        buffer.Count.Should().Be(3, "Peek는 데이터를 소비하지 않아야 함");
    }

    [Fact(DisplayName = "RingBuffer - Consume은 데이터를 제거한다")]
    public void RingBuffer_Consume_RemovesData()
    {
        // Given
        using var buffer = new RingBuffer(64);
        var testData = new byte[] { 10, 20, 30, 40, 50 };
        buffer.WriteBytes(testData);

        // When
        buffer.Consume(2); // 첫 2바이트 제거

        // Then
        buffer.Count.Should().Be(3, "2바이트가 소비되어 3바이트 남음");
        var remainingData = buffer.Peek(3);
        remainingData.ToArray().Should().Equal(new byte[] { 30, 40, 50 }, "소비 후 남은 데이터");
    }

    [Fact(DisplayName = "RingBuffer - GetWriteSpan + Advance로 직접 쓰기")]
    public void RingBuffer_GetWriteSpanAndAdvance_DirectWrite()
    {
        // Given
        using var buffer = new RingBuffer(64);
        var testData = new byte[] { 1, 2, 3, 4, 5 };

        // When
        var writeSpan = buffer.GetWriteSpan(testData.Length);
        testData.CopyTo(writeSpan);
        buffer.Advance(testData.Length);

        // Then
        buffer.Count.Should().Be(5);
        var readSpan = buffer.Peek(5);
        readSpan.ToArray().Should().Equal(testData);
    }

    [Fact(DisplayName = "RingBuffer - ReadBytes로 데이터 읽기 및 제거")]
    public void RingBuffer_ReadBytes_ReadsAndConsumes()
    {
        // Given
        using var buffer = new RingBuffer(64);
        var testData = new byte[] { 10, 20, 30, 40, 50 };
        buffer.WriteBytes(testData);

        // When
        var destination = new byte[3];
        var bytesRead = buffer.ReadBytes(destination);

        // Then
        bytesRead.Should().Be(3, "3바이트를 읽었어야 함");
        destination.Should().Equal(new byte[] { 10, 20, 30 }, "읽은 데이터");
        buffer.Count.Should().Be(2, "3바이트가 소비되어 2바이트 남음");
    }

    [Fact(DisplayName = "RingBuffer - WriteByte/ReadByte 단일 바이트 처리")]
    public void RingBuffer_SingleByteWriteRead_Works()
    {
        // Given
        using var buffer = new RingBuffer(16);

        // When
        buffer.WriteByte(100);
        buffer.WriteByte(200);
        var byte1 = buffer.ReadByte();
        var byte2 = buffer.ReadByte();

        // Then
        byte1.Should().Be(100);
        byte2.Should().Be(200);
        buffer.Count.Should().Be(0, "모든 데이터가 소비됨");
    }

    [Fact(DisplayName = "RingBuffer - PeekByte로 특정 오프셋 데이터 확인")]
    public void RingBuffer_PeekByte_ChecksSpecificOffset()
    {
        // Given
        using var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 10, 20, 30, 40, 50 });

        // When & Then
        buffer.PeekByte(0).Should().Be(10);
        buffer.PeekByte(2).Should().Be(30);
        buffer.PeekByte(4).Should().Be(50);
        buffer.Count.Should().Be(5, "PeekByte는 데이터를 소비하지 않음");
    }

    #endregion

    #region 순환 동작 테스트 (버퍼 끝에서 처음으로 래핑)

    [Fact(DisplayName = "RingBuffer - 순환 쓰기: 버퍼 끝에서 처음으로 래핑")]
    public void RingBuffer_CircularWrite_WrapsAroundBuffer()
    {
        // Given
        using var buffer = new RingBuffer(10); // 작은 버퍼
        var data1 = new byte[] { 1, 2, 3, 4, 5, 6 }; // 6 bytes
        var data2 = new byte[] { 7, 8, 9, 10 };      // 4 bytes

        // When
        buffer.WriteBytes(data1);     // [1,2,3,4,5,6,_,_,_,_]
        buffer.Consume(4);             // [_,_,_,_,5,6,_,_,_,_] (Tail=4, Count=2)
        buffer.WriteBytes(data2);      // [9,10,_,_,5,6,7,8,_,_] (wrapped)

        // Then
        buffer.Count.Should().Be(6, "2 + 4 = 6바이트");

        // 순차 읽기 검증
        var result = new byte[6];
        buffer.ReadBytes(result);
        result.Should().Equal(new byte[] { 5, 6, 7, 8, 9, 10 }, "래핑된 순서로 읽혀야 함");
    }

    [Fact(DisplayName = "RingBuffer - 순환 읽기: Peek와 Consume 조합")]
    public void RingBuffer_CircularRead_PeekAndConsume()
    {
        // Given
        using var buffer = new RingBuffer(8);
        buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6 }); // [1,2,3,4,5,6,_,_]
        buffer.Consume(4);                                   // [_,_,_,_,5,6,_,_] (Tail=4)
        buffer.WriteBytes(new byte[] { 7, 8, 9 });           // [8,9,_,_,5,6,7,_] (Head=2)

        // When
        var peek1 = buffer.Peek(3); // Tail=4부터 읽기 시작

        // Then
        peek1.ToArray().Should().Equal(new byte[] { 5, 6, 7 }, "연속된 부분만 Peek");

        // Consume 후 나머지 읽기
        buffer.Consume(3);
        var peek2 = buffer.Peek(2);
        peek2.ToArray().Should().Equal(new byte[] { 8, 9 }, "래핑된 부분 읽기");
    }

    [Fact(DisplayName = "RingBuffer - GetWriteSpan은 연속된 공간만 반환 (래핑 전)")]
    public void RingBuffer_GetWriteSpan_ReturnsContiguousSpace()
    {
        // Given
        using var buffer = new RingBuffer(10);
        var capacity = buffer.Capacity; // 실제 ArrayPool 할당 크기
        var writeSize = capacity / 2 + 2; // 절반보다 조금 더
        buffer.WriteBytes(new byte[writeSize]); // [1,2,3,4,5,6,...]
        buffer.Consume(4);                       // 앞 4바이트 소비 (Tail이동)

        // When
        // FreeSpace보다 큰 값을 요청하여 연속 공간만 받도록
        var requestSize = buffer.FreeSpace;
        var writeSpan = buffer.GetWriteSpan(requestSize);

        // Then
        var expectedContiguous = capacity - (writeSize); // Head부터 버퍼 끝까지
        writeSpan.Length.Should().Be(expectedContiguous, "Head부터 버퍼 끝까지 연속 공간만 반환");
    }

    #endregion

    #region 용량 초과 테스트

    [Fact(DisplayName = "RingBuffer - 용량 초과 시 Write는 예외 발생")]
    public void RingBuffer_WriteExceedsCapacity_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(10);
        var halfCapacity = buffer.Capacity / 2;
        buffer.WriteBytes(new byte[halfCapacity]); // 절반 사용

        // When
        var action = () => buffer.WriteBytes(new byte[buffer.Capacity]); // 전체 용량만큼 쓰기 시도 (초과)

        // Then
        action.Should().Throw<InvalidOperationException>("용량 초과 시 예외가 발생해야 함");
    }

    [Fact(DisplayName = "RingBuffer - Peek 요청이 Count 초과 시 예외 발생")]
    public void RingBuffer_PeekExceedsCount_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 1, 2, 3 });

        // When
        Action action = () => buffer.Peek(10); // Count=3인데 10바이트 요청

        // Then
        action.Should().Throw<InvalidOperationException>("Count 초과 Peek 시 예외 발생");
    }

    [Fact(DisplayName = "RingBuffer - Consume 요청이 Count 초과 시 예외 발생")]
    public void RingBuffer_ConsumeExceedsCount_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 1, 2, 3 });

        // When
        var action = () => buffer.Consume(10); // Count=3인데 10바이트 소비 시도

        // Then
        action.Should().Throw<InvalidOperationException>("Count 초과 Consume 시 예외 발생");
    }

    [Fact(DisplayName = "RingBuffer - 빈 버퍼에서 ReadByte 시 예외 발생")]
    public void RingBuffer_ReadByteFromEmpty_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(16);

        // When
        var action = () => buffer.ReadByte();

        // Then
        action.Should().Throw<InvalidOperationException>("빈 버퍼에서 ReadByte 시 예외 발생");
    }

    [Fact(DisplayName = "RingBuffer - 가득 찬 버퍼에서 WriteByte 시 예외 발생")]
    public void RingBuffer_WriteByteToFull_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(4);
        // 버퍼를 완전히 채우기 (실제 Capacity 사용)
        buffer.WriteBytes(new byte[buffer.Capacity]);

        // When
        Action action = () => buffer.WriteByte(5);

        // Then
        action.Should().Throw<InvalidOperationException>("가득 찬 버퍼에서 WriteByte 시 예외 발생");
    }

    [Fact(DisplayName = "RingBuffer - GetWriteSpan 요청 크기가 FreeSpace 초과 시 예외 발생")]
    public void RingBuffer_GetWriteSpanExceedsFreeSpace_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(10);
        var dataSize = buffer.Capacity - 2; // 전체 용량의 대부분 채우기
        buffer.WriteBytes(new byte[dataSize]);
        // FreeSpace = 2

        // When
        Action action = () => buffer.GetWriteSpan(buffer.FreeSpace + 1); // FreeSpace 초과 요청

        // Then
        action.Should().Throw<InvalidOperationException>("FreeSpace 초과 요청 시 예외 발생");
    }

    #endregion

    #region Clear 동작 테스트

    [Fact(DisplayName = "RingBuffer - Clear는 버퍼를 초기 상태로 리셋한다")]
    public void RingBuffer_Clear_ResetsToInitialState()
    {
        // Given
        using var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5 });
        buffer.Consume(2);

        // When
        buffer.Clear();

        // Then
        buffer.Count.Should().Be(0, "Clear 후 Count는 0");
        buffer.FreeSpace.Should().Be(64, "Clear 후 FreeSpace는 전체 용량");
    }

    [Fact(DisplayName = "RingBuffer - Clear 후 새 데이터 쓰기 가능")]
    public void RingBuffer_AfterClear_CanWriteNewData()
    {
        // Given
        using var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 1, 2, 3 });
        buffer.Clear();

        // When
        buffer.WriteBytes(new byte[] { 10, 20, 30 });
        var data = buffer.Peek(3);

        // Then
        data.ToArray().Should().Equal(new byte[] { 10, 20, 30 }, "Clear 후 새 데이터 쓰기 성공");
    }

    #endregion

    #region 에지 케이스 테스트

    [Fact(DisplayName = "RingBuffer - 빈 데이터 Write는 무시됨")]
    public void RingBuffer_WriteEmptyData_Ignored()
    {
        // Given
        using var buffer = new RingBuffer(64);

        // When
        buffer.WriteBytes(ReadOnlySpan<byte>.Empty);

        // Then
        buffer.Count.Should().Be(0, "빈 데이터 Write는 무시");
    }

    [Fact(DisplayName = "RingBuffer - Peek(0)은 빈 Span 반환")]
    public void RingBuffer_PeekZero_ReturnsEmpty()
    {
        // Given
        using var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 1, 2, 3 });

        // When
        var peek = buffer.Peek(0);

        // Then
        peek.Length.Should().Be(0, "Peek(0)은 빈 Span 반환");
    }

    [Fact(DisplayName = "RingBuffer - Consume(0)은 아무 동작 안 함")]
    public void RingBuffer_ConsumeZero_DoesNothing()
    {
        // Given
        using var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 1, 2, 3 });

        // When
        buffer.Consume(0);

        // Then
        buffer.Count.Should().Be(3, "Consume(0)은 아무 영향 없음");
    }

    [Fact(DisplayName = "RingBuffer - 버퍼를 완전히 채우고 비우기")]
    public void RingBuffer_FillAndEmpty_Works()
    {
        // Given
        using var buffer = new RingBuffer(8);
        var capacity = buffer.Capacity; // 실제 ArrayPool 할당 크기

        // When
        buffer.WriteBytes(new byte[capacity]); // 완전히 채움
        buffer.Consume(capacity); // 완전히 비움

        // Then
        buffer.Count.Should().Be(0);
        buffer.FreeSpace.Should().Be(capacity);

        // 다시 쓰기
        buffer.WriteBytes(new byte[] { 10, 20 });
        buffer.Count.Should().Be(2);
    }

    [Fact(DisplayName = "RingBuffer - 여러 번 순환 쓰기/읽기")]
    public void RingBuffer_MultipleCircularOperations_Works()
    {
        // Given
        using var buffer = new RingBuffer(8);

        // When/Then - 여러 번 순환
        for (int i = 0; i < 5; i++)
        {
            buffer.WriteBytes(new byte[] { (byte)(i * 10), (byte)(i * 10 + 1) });
            var data = new byte[2];
            buffer.ReadBytes(data);
            data.Should().Equal(new byte[] { (byte)(i * 10), (byte)(i * 10 + 1) });
        }

        buffer.Count.Should().Be(0, "모든 순환 후 버퍼는 비어있어야 함");
    }

    #endregion

    #region Dispose 테스트

    [Fact(DisplayName = "RingBuffer - Dispose는 정상적으로 완료된다")]
    public void RingBuffer_Dispose_CompletesSuccessfully()
    {
        // Given
        var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 1, 2, 3 });

        // When
        var action = () => buffer.Dispose();

        // Then
        action.Should().NotThrow("Dispose가 정상적으로 완료되어야 함");
    }

    [Fact(DisplayName = "RingBuffer - 여러 번 Dispose 해도 안전하다")]
    public void RingBuffer_MultipleDispose_IsSafe()
    {
        // Given
        var buffer = new RingBuffer(64);

        // When
        buffer.Dispose();
        var action = () => buffer.Dispose();

        // Then
        action.Should().NotThrow("여러 번 Dispose 해도 안전해야 함");
    }

    #endregion

    #region 잘못된 인자 테스트

    [Fact(DisplayName = "RingBuffer - 음수 Capacity는 예외 발생")]
    public void RingBuffer_NegativeCapacity_ThrowsException()
    {
        // When
        var action = () => new RingBuffer(-1);

        // Then
        action.Should().Throw<ArgumentException>("음수 Capacity는 허용되지 않음");
    }

    [Fact(DisplayName = "RingBuffer - 0 Capacity는 예외 발생")]
    public void RingBuffer_ZeroCapacity_ThrowsException()
    {
        // When
        var action = () => new RingBuffer(0);

        // Then
        action.Should().Throw<ArgumentException>("0 Capacity는 허용되지 않음");
    }

    [Fact(DisplayName = "RingBuffer - GetWriteSpan에 음수 크기 요청 시 예외 발생")]
    public void RingBuffer_GetWriteSpanNegativeSize_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(64);

        // When
        Action action = () => buffer.GetWriteSpan(-1);

        // Then
        action.Should().Throw<ArgumentException>("음수 크기는 허용되지 않음");
    }

    [Fact(DisplayName = "RingBuffer - Advance에 음수 값 전달 시 예외 발생")]
    public void RingBuffer_AdvanceNegative_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(64);

        // When
        var action = () => buffer.Advance(-1);

        // Then
        action.Should().Throw<ArgumentException>("음수 Advance는 허용되지 않음");
    }

    [Fact(DisplayName = "RingBuffer - Advance가 FreeSpace 초과 시 예외 발생")]
    public void RingBuffer_AdvanceExceedsFreeSpace_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(10);
        buffer.WriteBytes(new byte[] { 1, 2, 3 }); // FreeSpace 감소

        // When
        Action action = () => buffer.Advance(buffer.FreeSpace + 1); // FreeSpace 초과

        // Then
        action.Should().Throw<InvalidOperationException>("FreeSpace 초과 Advance는 예외 발생");
    }

    [Fact(DisplayName = "RingBuffer - Peek 음수 Count는 예외 발생")]
    public void RingBuffer_PeekNegativeCount_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(64);

        // When
        Action action = () => buffer.Peek(-1);

        // Then
        action.Should().Throw<ArgumentException>("음수 Count는 허용되지 않음");
    }

    [Fact(DisplayName = "RingBuffer - Consume 음수 Count는 예외 발생")]
    public void RingBuffer_ConsumeNegativeCount_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(64);

        // When
        var action = () => buffer.Consume(-1);

        // Then
        action.Should().Throw<ArgumentException>("음수 Count는 허용되지 않음");
    }

    [Fact(DisplayName = "RingBuffer - PeekByte 범위 밖 오프셋 시 예외 발생")]
    public void RingBuffer_PeekByteOutOfRange_ThrowsException()
    {
        // Given
        using var buffer = new RingBuffer(64);
        buffer.WriteBytes(new byte[] { 1, 2, 3 });

        // When
        var action = () => buffer.PeekByte(10); // Count=3인데 offset=10

        // Then
        action.Should().Throw<ArgumentOutOfRangeException>("범위 밖 오프셋은 예외 발생");
    }

    #endregion
}
