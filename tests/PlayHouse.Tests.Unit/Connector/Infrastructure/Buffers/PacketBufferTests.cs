#nullable enable

using System;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using PlayHouse.Connector.Infrastructure.Buffers;
using Xunit;

namespace PlayHouse.Tests.Unit.Connector.Infrastructure.Buffers;

/// <summary>
/// 단위 테스트: PacketBuffer의 Write/Read, Flip/Clear/Compact, 자동 확장, Zero-copy 검증
/// </summary>
public class PacketBufferTests
{
    #region Write/Read 왕복 테스트

    [Fact(DisplayName = "PacketBuffer - Byte Write/Read 왕복 테스트")]
    public void PacketBuffer_ByteWriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        byte expectedValue = 42;

        // When
        buffer.WriteByte(expectedValue);
        buffer.Flip();
        var actualValue = buffer.ReadByte();

        // Then
        actualValue.Should().Be(expectedValue, "Write한 값과 Read한 값이 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - Int16 Write/Read 왕복 테스트 (Little-Endian)")]
    public void PacketBuffer_Int16WriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        short expectedValue = -12345;

        // When
        buffer.WriteInt16(expectedValue);
        buffer.Flip();
        var actualValue = buffer.ReadInt16();

        // Then
        actualValue.Should().Be(expectedValue, "Write한 값과 Read한 값이 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - UInt16 Write/Read 왕복 테스트 (Little-Endian)")]
    public void PacketBuffer_UInt16WriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        ushort expectedValue = 54321;

        // When
        buffer.WriteUInt16(expectedValue);
        buffer.Flip();
        var actualValue = buffer.ReadUInt16();

        // Then
        actualValue.Should().Be(expectedValue, "Write한 값과 Read한 값이 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - Int32 Write/Read 왕복 테스트 (Little-Endian)")]
    public void PacketBuffer_Int32WriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        int expectedValue = -1234567890;

        // When
        buffer.WriteInt32(expectedValue);
        buffer.Flip();
        var actualValue = buffer.ReadInt32();

        // Then
        actualValue.Should().Be(expectedValue, "Write한 값과 Read한 값이 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - Int64 Write/Read 왕복 테스트 (Little-Endian)")]
    public void PacketBuffer_Int64WriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        long expectedValue = -1234567890123456789L;

        // When
        buffer.WriteInt64(expectedValue);
        buffer.Flip();
        var actualValue = buffer.ReadInt64();

        // Then
        actualValue.Should().Be(expectedValue, "Write한 값과 Read한 값이 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - String Write/Read 왕복 테스트 (UTF-8)")]
    public void PacketBuffer_StringWriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(256);
        var expectedValue = "Hello, 안녕하세요!";

        // When
        buffer.WriteString(expectedValue);
        buffer.Flip();
        var actualValue = buffer.ReadString();

        // Then
        actualValue.Should().Be(expectedValue, "Write한 문자열과 Read한 문자열이 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - 빈 문자열 Write/Read 왕복 테스트")]
    public void PacketBuffer_EmptyStringWriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        var expectedValue = string.Empty;

        // When
        buffer.WriteString(expectedValue);
        buffer.Flip();
        var actualValue = buffer.ReadString();

        // Then
        actualValue.Should().Be(expectedValue, "빈 문자열도 정상적으로 처리되어야 함");
    }

    [Fact(DisplayName = "PacketBuffer - Bytes Write/Read 왕복 테스트")]
    public void PacketBuffer_BytesWriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(64);
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        // When
        buffer.WriteBytes(expectedBytes);
        buffer.Flip();
        var actualBytes = buffer.ReadBytes(expectedBytes.Length);

        // Then
        actualBytes.ToArray().Should().Equal(expectedBytes, "Write한 바이트 배열과 Read한 바이트 배열이 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - 복합 데이터 Write/Read 왕복 테스트")]
    public void PacketBuffer_ComplexDataWriteRead_RoundTrip()
    {
        // Given
        using var buffer = new PacketBuffer(256);
        byte expectedByte = 100;
        int expectedInt = 42000;
        long expectedLong = 9876543210L;
        var expectedString = "테스트";
        var expectedBytes = new byte[] { 10, 20, 30 };

        // When
        buffer.WriteByte(expectedByte);
        buffer.WriteInt32(expectedInt);
        buffer.WriteInt64(expectedLong);
        buffer.WriteString(expectedString);
        buffer.WriteBytes(expectedBytes);
        buffer.Flip();

        var actualByte = buffer.ReadByte();
        var actualInt = buffer.ReadInt32();
        var actualLong = buffer.ReadInt64();
        var actualString = buffer.ReadString();
        var actualBytes = buffer.ReadBytes(expectedBytes.Length);

        // Then
        actualByte.Should().Be(expectedByte);
        actualInt.Should().Be(expectedInt);
        actualLong.Should().Be(expectedLong);
        actualString.Should().Be(expectedString);
        actualBytes.ToArray().Should().Equal(expectedBytes);
    }

    #endregion

    #region Flip/Clear/Compact 동작 테스트

    [Fact(DisplayName = "PacketBuffer - Flip은 Write 모드를 Read 모드로 전환한다")]
    public void PacketBuffer_Flip_SwitchesFromWriteModeToReadMode()
    {
        // Given
        using var buffer = new PacketBuffer(64);
        buffer.WriteInt32(100);
        buffer.WriteInt32(200);

        // When
        buffer.Flip();

        // Then
        buffer.Position.Should().Be(0, "Flip 후 Position은 0이어야 함");
        buffer.Limit.Should().Be(8, "Flip 후 Limit은 이전 Position이어야 함 (4 + 4 bytes)");
        buffer.Remaining.Should().Be(8, "Flip 후 Remaining은 Limit과 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - Clear는 버퍼를 초기 상태로 리셋한다")]
    public void PacketBuffer_Clear_ResetsBufferToInitialState()
    {
        // Given
        using var buffer = new PacketBuffer(64);
        buffer.WriteInt32(100);
        buffer.Flip();
        buffer.ReadInt32();

        // When
        buffer.Clear();

        // Then
        buffer.Position.Should().Be(0, "Clear 후 Position은 0이어야 함");
        buffer.Limit.Should().Be(buffer.Capacity, "Clear 후 Limit은 Capacity와 같아야 함");
        buffer.Remaining.Should().Be(buffer.Capacity, "Clear 후 Remaining은 Capacity와 같아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - Compact는 남은 데이터를 버퍼 시작으로 이동한다")]
    public void PacketBuffer_Compact_MovesRemainingDataToBeginning()
    {
        // Given
        using var buffer = new PacketBuffer(64);
        buffer.WriteInt32(100); // 4 bytes
        buffer.WriteInt32(200); // 4 bytes
        buffer.WriteInt32(300); // 4 bytes
        buffer.Flip();
        buffer.ReadInt32(); // Read 100, Position = 4

        // When
        buffer.Compact();

        // Then
        buffer.Position.Should().Be(8, "Compact 후 Position은 남은 데이터 크기여야 함 (8 bytes)");
        buffer.Limit.Should().Be(buffer.Capacity, "Compact 후 Limit은 Capacity여야 함");

        // 남은 데이터 검증
        buffer.Flip();
        buffer.ReadInt32().Should().Be(200, "첫 번째 남은 데이터는 200이어야 함");
        buffer.ReadInt32().Should().Be(300, "두 번째 남은 데이터는 300이어야 함");
    }

    #endregion

    #region 버퍼 자동 확장 테스트

    [Fact(DisplayName = "PacketBuffer - 용량 초과 시 자동으로 확장된다")]
    public void PacketBuffer_AutoExpand_WhenCapacityExceeded()
    {
        // Given
        using var buffer = new PacketBuffer(8); // 작은 초기 용량
        var initialCapacity = buffer.Capacity;

        // When
        for (int i = 0; i < 100; i++)
        {
            buffer.WriteInt32(i);
        }

        // Then
        buffer.Capacity.Should().BeGreaterThan(initialCapacity, "용량이 자동으로 확장되어야 함");
        buffer.Position.Should().Be(400, "100개의 Int32 = 400 bytes");
    }

    [Fact(DisplayName = "PacketBuffer - 자동 확장 후에도 데이터는 유지된다")]
    public void PacketBuffer_AutoExpand_PreservesData()
    {
        // Given
        using var buffer = new PacketBuffer(8);
        var testData = new int[] { 10, 20, 30, 40, 50 };

        // When
        foreach (var value in testData)
        {
            buffer.WriteInt32(value);
        }
        buffer.Flip();

        // Then
        foreach (var expected in testData)
        {
            buffer.ReadInt32().Should().Be(expected, "자동 확장 후에도 데이터가 유지되어야 함");
        }
    }

    #endregion

    #region Zero-copy 확인 테스트

    [Fact(DisplayName = "PacketBuffer - AsSpan은 Zero-copy로 데이터를 반환한다")]
    public void PacketBuffer_AsSpan_ReturnsZeroCopyData()
    {
        // Given
        using var buffer = new PacketBuffer(64);
        buffer.WriteInt32(100);
        buffer.WriteInt32(200);

        // When
        var span = buffer.AsSpan();

        // Then
        span.Length.Should().Be(8, "AsSpan은 현재 Position까지의 데이터를 반환해야 함");

        // Verify Little-Endian encoding
        var value1 = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
        var value2 = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));

        value1.Should().Be(100);
        value2.Should().Be(200);
    }

    [Fact(DisplayName = "PacketBuffer - AsSpan(offset, length)는 부분 데이터를 Zero-copy로 반환한다")]
    public void PacketBuffer_AsSpanWithRange_ReturnsZeroCopyPartialData()
    {
        // Given
        using var buffer = new PacketBuffer(64);
        buffer.WriteInt32(100);
        buffer.WriteInt32(200);
        buffer.WriteInt32(300);

        // When
        var span = buffer.AsSpan(4, 4); // 두 번째 Int32만 가져오기

        // Then
        span.Length.Should().Be(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(span);
        value.Should().Be(200);
    }

    [Fact(DisplayName = "PacketBuffer - AsMemory는 Zero-copy로 데이터를 반환한다")]
    public void PacketBuffer_AsMemory_ReturnsZeroCopyData()
    {
        // Given
        using var buffer = new PacketBuffer(64);
        buffer.WriteInt32(100);
        buffer.WriteInt32(200);

        // When
        var memory = buffer.AsMemory();

        // Then
        memory.Length.Should().Be(8, "AsMemory는 현재 Position까지의 데이터를 반환해야 함");
    }

    [Fact(DisplayName = "PacketBuffer - GetWriteSpan으로 직접 쓰기 가능")]
    public void PacketBuffer_GetWriteSpan_AllowsDirectWrite()
    {
        // Given
        using var buffer = new PacketBuffer(64);
        var testData = new byte[] { 1, 2, 3, 4, 5 };

        // When
        var writeSpan = buffer.GetWriteSpan(testData.Length);
        testData.CopyTo(writeSpan);
        buffer.Advance(testData.Length);
        buffer.Flip();

        // Then
        var readSpan = buffer.ReadBytes(testData.Length);
        readSpan.ToArray().Should().Equal(testData, "직접 쓴 데이터가 정상적으로 읽혀야 함");
    }

    #endregion

    #region Little-Endian 바이트 순서 확인

    [Fact(DisplayName = "PacketBuffer - Int16은 Little-Endian으로 저장된다")]
    public void PacketBuffer_Int16_StoredInLittleEndian()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        short value = 0x1234; // Binary: 00010010 00110100

        // When
        buffer.WriteInt16(value);
        var span = buffer.AsSpan();

        // Then
        span[0].Should().Be(0x34, "Little-Endian이므로 하위 바이트가 먼저");
        span[1].Should().Be(0x12, "Little-Endian이므로 상위 바이트가 나중");
    }

    [Fact(DisplayName = "PacketBuffer - Int32는 Little-Endian으로 저장된다")]
    public void PacketBuffer_Int32_StoredInLittleEndian()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        int value = 0x12345678;

        // When
        buffer.WriteInt32(value);
        var span = buffer.AsSpan();

        // Then
        span[0].Should().Be(0x78, "Little-Endian 바이트 순서");
        span[1].Should().Be(0x56);
        span[2].Should().Be(0x34);
        span[3].Should().Be(0x12);
    }

    [Fact(DisplayName = "PacketBuffer - Int64는 Little-Endian으로 저장된다")]
    public void PacketBuffer_Int64_StoredInLittleEndian()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        long value = 0x123456789ABCDEF0L;

        // When
        buffer.WriteInt64(value);
        var span = buffer.AsSpan();

        // Then
        span[0].Should().Be(0xF0, "Little-Endian 바이트 순서");
        span[1].Should().Be(0xDE);
        span[2].Should().Be(0xBC);
        span[3].Should().Be(0x9A);
        span[4].Should().Be(0x78);
        span[5].Should().Be(0x56);
        span[6].Should().Be(0x34);
        span[7].Should().Be(0x12);
    }

    #endregion

    #region Wrap 기능 테스트

    [Fact]
    public void PacketBuffer_Wrap_WrapsExistingArrayZeroCopy()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        // Act
        using var buffer = PacketBuffer.Wrap(data, 2, 4);

        // Assert
        buffer.Position.Should().Be(2);
        buffer.Limit.Should().Be(6);
        buffer.Remaining.Should().Be(4);
        buffer.ReadByte().Should().Be(0x03);
        buffer.ReadByte().Should().Be(0x04);
    }

    [Fact]
    public void PacketBuffer_Wrap_CannotExpand()
    {
        // Arrange
        var data = new byte[4];
        using var buffer = PacketBuffer.Wrap(data, 0, 4);
        buffer.Position = 0;

        // Act & Assert - Wrapped buffer should not expand
        buffer.WriteInt32(0x12345678); // 4 bytes, exactly at limit
        var act = () => buffer.WriteByte(0xFF); // Should fail - cannot expand

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot expand wrapped buffer*");
    }

    #endregion

    #region 에러 케이스 테스트

    [Fact(DisplayName = "PacketBuffer - Read 시 데이터 부족하면 예외 발생")]
    public void PacketBuffer_ReadBeyondLimit_ThrowsException()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        buffer.WriteInt32(100);
        buffer.Flip();
        buffer.ReadInt32(); // 모든 데이터 읽음

        // When
        var action = () => buffer.ReadInt32(); // 더 읽으려고 시도

        // Then
        action.Should().Throw<InvalidOperationException>("데이터가 부족하면 예외가 발생해야 함");
    }

    [Fact(DisplayName = "PacketBuffer - 255바이트 초과 문자열은 예외 발생")]
    public void PacketBuffer_WriteStringTooLarge_ThrowsException()
    {
        // Given
        using var buffer = new PacketBuffer(512);
        var longString = new string('A', 300); // 255바이트 초과

        // When
        var action = () => buffer.WriteString(longString);

        // Then
        action.Should().Throw<ArgumentException>("255바이트 초과 문자열은 예외가 발생해야 함");
    }

    [Fact(DisplayName = "PacketBuffer - 음수 용량으로 생성 시 예외 발생")]
    public void PacketBuffer_NegativeCapacity_ThrowsException()
    {
        // When
        var action = () => new PacketBuffer(-1);

        // Then
        action.Should().Throw<ArgumentException>("음수 용량은 허용되지 않아야 함");
    }

    [Fact(DisplayName = "PacketBuffer - Position을 Limit 초과로 설정 시 예외 발생")]
    public void PacketBuffer_SetPositionBeyondLimit_ThrowsException()
    {
        // Given
        using var buffer = new PacketBuffer(16);
        buffer.WriteInt32(100);
        buffer.Flip();

        // When
        var action = () => buffer.Position = 100; // Limit 초과

        // Then
        action.Should().Throw<ArgumentOutOfRangeException>("Position은 Limit을 초과할 수 없어야 함");
    }

    [Fact(DisplayName = "PacketBuffer - Advance를 Limit 초과로 호출 시 예외 발생")]
    public void PacketBuffer_AdvanceBeyondLimit_ThrowsException()
    {
        // Given
        using var buffer = new PacketBuffer(16);

        // When
        var action = () => buffer.Advance(100); // Limit 초과

        // Then
        action.Should().Throw<ArgumentException>("Advance는 Limit을 초과할 수 없어야 함");
    }

    #endregion

    #region Dispose 테스트

    [Fact(DisplayName = "PacketBuffer - Dispose는 정상적으로 완료된다")]
    public void PacketBuffer_Dispose_CompletesSuccessfully()
    {
        // Given
        var buffer = new PacketBuffer(16);
        buffer.WriteInt32(100);

        // When
        var action = () => buffer.Dispose();

        // Then
        action.Should().NotThrow("Dispose가 정상적으로 완료되어야 함");
    }

    [Fact(DisplayName = "PacketBuffer - 여러 번 Dispose 해도 안전하다")]
    public void PacketBuffer_MultipleDispose_IsSafe()
    {
        // Given
        var buffer = new PacketBuffer(16);

        // When
        buffer.Dispose();
        var action = () => buffer.Dispose();

        // Then
        action.Should().NotThrow("여러 번 Dispose 해도 안전해야 함");
    }

    #endregion
}
