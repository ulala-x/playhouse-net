using System.Reflection;
using FluentAssertions;
using PlayHouse.Infrastructure.Memory;
using Xunit;

namespace PlayHouse.Unit.Infrastructure.Memory;

public class MessagePoolTests
{
    [Theory]
    [InlineData(1, 128)]
    [InlineData(128, 128)]
    [InlineData(129, 256)]
    [InlineData(1024, 1024)]
    [InlineData(1025, 2048)]
    [InlineData(8192, 8192)]
    [InlineData(8193, 12288)]
    [InlineData(65536, 65536)]
    [InlineData(65537, 81920)]
    public void Rent_ShouldReturnBufferWithCorrectSize(int requestedSize, int expectedBufferSize)
    {
        // Act
        var buffer = MessagePool.Rent(requestedSize);

        // Assert
        buffer.Length.Should().Be(expectedBufferSize);
        
        // Cleanup
        MessagePool.Return(buffer);
    }

    [Fact]
    public void Rent_And_Return_ShouldUseLocalCache()
    {
        // Arrange
        var buffer1 = MessagePool.Rent(256);
        MessagePool.Return(buffer1);

        // Act
        var buffer2 = MessagePool.Rent(256);

        // Assert
        buffer2.Should().BeSameAs(buffer1, "Returned buffer should be reused from local cache");
    }

    [Fact]
    public void GetBucketIndex_ShouldMatchCGDKLogic()
    {
        // Tiny (~1K)
        MessagePool.GetBucketIndex(1).Should().Be(0);
        MessagePool.GetBucketIndex(1024).Should().Be(7);

        // Small (~8K)
        MessagePool.GetBucketIndex(1025).Should().Be(8);
        MessagePool.GetBucketIndex(8192).Should().Be(14);

        // Medium (~64K)
        MessagePool.GetBucketIndex(8193).Should().Be(15);
        MessagePool.GetBucketIndex(65536).Should().Be(28);

        // Large (~311K)
        MessagePool.GetBucketIndex(65537).Should().Be(29);
        MessagePool.GetBucketIndex(262144).Should().Be(40); // 256KB is at index 40 (64K + 12*16K)

        // Huge (~1M)
        MessagePool.GetBucketIndex(311297).Should().Be(44);
        MessagePool.GetBucketIndex(966656).Should().Be(52);
    }

    [Fact]
    public void Rent_HugeSize_ShouldReturnDirectAllocation()
    {
        // Arrange
        int hugeSize = 2 * 1024 * 1024; // 2MB

        // Act
        var buffer = MessagePool.Rent(hugeSize);

        // Assert
        buffer.Length.Should().Be(hugeSize);
        
        // Return should not throw but won't be pooled
        MessagePool.Return(buffer);
    }

    [Fact]
    public void WarmUp_ShouldPopulateGlobalPool()
    {
        // Arrange
        var config = new MessagePoolConfig
        {
            TinyWarmUpCount = 10,
            SmallWarmUpCount = 5,
            MediumWarmUpCount = 0,
            LargeWarmUpCount = 0
        };
        MessagePool.ApplyConfig(config);

        // Act
        MessagePool.WarmUp();

        // Assert
        // Reflection to check private field for verification
        var globalPoolField = typeof(MessagePool).GetField("_globalPool", BindingFlags.Static | BindingFlags.NonPublic);
        var globalPool = (Array)globalPoolField!.GetValue(null)!;
        
        // Bucket 0 (128B) should have at least 10 items
        var bucket0 = globalPool.GetValue(0);
        var countProperty = bucket0!.GetType().GetProperty("Count");
        ((int)countProperty!.GetValue(bucket0)!).Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public void AutoTrim_ShouldReducePoolSizeAfterInactivity()
    {
        // Arrange
        var config = new MessagePoolConfig
        {
            EnableAutoTrim = true,
            TinyWarmUpCount = 5,
            MaxTinyCount = 100,
            IdleThreshold = TimeSpan.FromMilliseconds(100), // Very short for testing
            TrimCheckInterval = TimeSpan.FromMilliseconds(50)
        };
        MessagePool.ApplyConfig(config);
        
        // Fill pool beyond warmup count
        var buffers = new List<byte[]>();
        for (int i = 0; i < 20; i++) buffers.Add(MessagePool.Rent(128));
        foreach (var b in buffers) MessagePool.Return(b);

        // Act
        // Wait for idle threshold and trim timer
        Thread.Sleep(500);

        // Assert
        var globalPoolField = typeof(MessagePool).GetField("_globalPool", BindingFlags.Static | BindingFlags.NonPublic);
        var globalPool = (Array)globalPoolField!.GetValue(null)!;
        var bucket0 = globalPool.GetValue(0);
        var countProperty = bucket0!.GetType().GetProperty("Count");
        int count = (int)countProperty!.GetValue(bucket0)!;
        
        // Should have shrunk towards 5 (might take multiple cycles, but should be less than 20)
        count.Should().BeLessThan(20, "Pool should have been trimmed after inactivity");
    }

    [Fact]
    public void MessagePoolPayload_ShouldReturnToPoolOnDispose()
    {
        // Arrange
        var buffer = MessagePool.Rent(1024);
        var payload = MessagePoolPayload.Create(buffer, 1024);

        // Act
        payload.Dispose();

        // Assert
        // After dispose, buffer should be back in MessagePool
        var rentedAgain = MessagePool.Rent(1024);
        rentedAgain.Should().BeSameAs(buffer, "Buffer should be returned to pool and reused");
    }
}