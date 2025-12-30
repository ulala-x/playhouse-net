#nullable enable

using System;
using System.Diagnostics;
using PlayHouse.Connector.Infrastructure.Buffers;
using Xunit;
using Xunit.Abstractions;

namespace PlayHouse.Tests.Unit.Connector.Infrastructure.Buffers;

/// <summary>
/// 성능 벤치마크: PooledBuffer vs PacketBuffer 비교
/// 콘솔 출력으로 성능 차이를 확인
/// </summary>
public class BufferBenchmark
{
    private readonly ITestOutputHelper _output;

    public BufferBenchmark(ITestOutputHelper output)
    {
        _output = output;
    }

    private const int Iterations = 10_000;

    [Fact(DisplayName = "벤치마크 - PooledBuffer vs PacketBuffer Write 성능 비교")]
    public void Benchmark_WritePerformance_Comparison()
    {
        _output.WriteLine("=== Write Performance Benchmark ===");
        _output.WriteLine($"Iterations: {Iterations:N0}");
        _output.WriteLine("");

        // Warmup
        BenchmarkPooledBufferWrite(1000);
        BenchmarkPacketBufferWrite(1000);

        // PooledBuffer Write
        var pooledTime = BenchmarkPooledBufferWrite(Iterations);
        _output.WriteLine($"PooledBuffer Write (Int32 x {Iterations:N0}): {pooledTime.TotalMilliseconds:F3} ms");

        // PacketBuffer Write
        var packetTime = BenchmarkPacketBufferWrite(Iterations);
        _output.WriteLine($"PacketBuffer Write (Int32 x {Iterations:N0}): {packetTime.TotalMilliseconds:F3} ms");

        // Comparison
        var improvement = ((pooledTime.TotalMilliseconds - packetTime.TotalMilliseconds) / pooledTime.TotalMilliseconds) * 100;
        _output.WriteLine("");
        _output.WriteLine($"Performance: {(improvement > 0 ? "PacketBuffer faster" : "PooledBuffer faster")} by {Math.Abs(improvement):F2}%");
    }

    [Fact(DisplayName = "벤치마크 - PooledBuffer vs PacketBuffer Read 성능 비교")]
    public void Benchmark_ReadPerformance_Comparison()
    {
        _output.WriteLine("=== Read Performance Benchmark ===");
        _output.WriteLine($"Iterations: {Iterations:N0}");
        _output.WriteLine("");

        // Warmup
        BenchmarkPooledBufferRead(1000);
        BenchmarkPacketBufferRead(1000);

        // PooledBuffer Read
        var pooledTime = BenchmarkPooledBufferRead(Iterations);
        _output.WriteLine($"PooledBuffer Read (Int32 x {Iterations:N0}): {pooledTime.TotalMilliseconds:F3} ms");

        // PacketBuffer Read
        var packetTime = BenchmarkPacketBufferRead(Iterations);
        _output.WriteLine($"PacketBuffer Read (Int32 x {Iterations:N0}): {packetTime.TotalMilliseconds:F3} ms");

        // Comparison
        var improvement = ((pooledTime.TotalMilliseconds - packetTime.TotalMilliseconds) / pooledTime.TotalMilliseconds) * 100;
        _output.WriteLine("");
        _output.WriteLine($"Performance: {(improvement > 0 ? "PacketBuffer faster" : "PooledBuffer faster")} by {Math.Abs(improvement):F2}%");
    }

    [Fact(DisplayName = "벤치마크 - 복합 작업 성능 비교")]
    public void Benchmark_ComplexOperations_Comparison()
    {
        _output.WriteLine("=== Complex Operations Benchmark ===");
        _output.WriteLine($"Iterations: {Iterations:N0}");
        _output.WriteLine("Operations per iteration: Write Int32 + Int64 + String + Bytes, then Read all");
        _output.WriteLine("");

        // Warmup
        BenchmarkPooledBufferComplex(100);
        BenchmarkPacketBufferComplex(100);

        // PooledBuffer Complex
        var pooledTime = BenchmarkPooledBufferComplex(Iterations);
        _output.WriteLine($"PooledBuffer Complex: {pooledTime.TotalMilliseconds:F3} ms");

        // PacketBuffer Complex
        var packetTime = BenchmarkPacketBufferComplex(Iterations);
        _output.WriteLine($"PacketBuffer Complex: {packetTime.TotalMilliseconds:F3} ms");

        // Comparison
        var improvement = ((pooledTime.TotalMilliseconds - packetTime.TotalMilliseconds) / pooledTime.TotalMilliseconds) * 100;
        _output.WriteLine("");
        _output.WriteLine($"Performance: {(improvement > 0 ? "PacketBuffer faster" : "PooledBuffer faster")} by {Math.Abs(improvement):F2}%");
    }

    [Fact(DisplayName = "벤치마크 - 메모리 할당 비교")]
    public void Benchmark_MemoryAllocation_Comparison()
    {
        _output.WriteLine("=== Memory Allocation Benchmark ===");
        _output.WriteLine($"Iterations: {Iterations:N0}");
        _output.WriteLine("");

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // PooledBuffer Memory
        var pooledMemBefore = GC.GetTotalMemory(false);
        var pooledTime = BenchmarkPooledBufferMemory(Iterations);
        var pooledMemAfter = GC.GetTotalMemory(false);
        var pooledMemDelta = pooledMemAfter - pooledMemBefore;

        _output.WriteLine($"PooledBuffer - Time: {pooledTime.TotalMilliseconds:F3} ms, Memory Delta: {pooledMemDelta:N0} bytes");

        // Force GC before next measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // PacketBuffer Memory
        var packetMemBefore = GC.GetTotalMemory(false);
        var packetTime = BenchmarkPacketBufferMemory(Iterations);
        var packetMemAfter = GC.GetTotalMemory(false);
        var packetMemDelta = packetMemAfter - packetMemBefore;

        _output.WriteLine($"PacketBuffer - Time: {packetTime.TotalMilliseconds:F3} ms, Memory Delta: {packetMemDelta:N0} bytes");

        // Comparison
        _output.WriteLine("");
        _output.WriteLine($"Memory: {(packetMemDelta < pooledMemDelta ? "PacketBuffer uses less" : "PooledBuffer uses less")} by {Math.Abs(pooledMemDelta - packetMemDelta):N0} bytes");
    }

    #region PooledBuffer Benchmarks

    private TimeSpan BenchmarkPooledBufferWrite(int iterations)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            using var buffer = new PooledBuffer(64);
            for (int j = 0; j < 10; j++)
            {
                AppendInt32(buffer, i + j);
            }
        }

        sw.Stop();
        return sw.Elapsed;
    }

    private TimeSpan BenchmarkPooledBufferRead(int iterations)
    {
        // Prepare data
        var buffers = new PooledBuffer[iterations];
        for (int i = 0; i < iterations; i++)
        {
            buffers[i] = new PooledBuffer(64);
            for (int j = 0; j < 10; j++)
            {
                AppendInt32(buffers[i], i + j);
            }
        }

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var buffer = buffers[i];
            int offset = 0;
            for (int j = 0; j < 10; j++)
            {
                ReadInt32(buffer, offset);
                offset += 4;
            }
        }

        sw.Stop();

        // Cleanup
        foreach (var buffer in buffers)
        {
            buffer.Dispose();
        }

        return sw.Elapsed;
    }

    private TimeSpan BenchmarkPooledBufferComplex(int iterations)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            using var buffer = new PooledBuffer(256);

            // Write
            AppendInt32(buffer, i);
            AppendInt64(buffer, i * 1000L);
            AppendString(buffer, "Test");
            AppendBytes(buffer, new byte[] { 1, 2, 3, 4, 5 });

            // Read
            int offset = 0;
            ReadInt32(buffer, offset);
            offset += 4;
            ReadInt64(buffer, offset);
            offset += 8;
            ReadString(buffer, offset, 4);
            offset += 4;
            // Skip bytes read
        }

        sw.Stop();
        return sw.Elapsed;
    }

    private TimeSpan BenchmarkPooledBufferMemory(int iterations)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            using var buffer = new PooledBuffer(64);
            AppendInt32(buffer, i);
            AppendInt64(buffer, i * 1000L);
            AppendString(buffer, "Memory Test");
        }

        sw.Stop();
        return sw.Elapsed;
    }

    #endregion

    #region PacketBuffer Benchmarks

    private TimeSpan BenchmarkPacketBufferWrite(int iterations)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            using var buffer = new PacketBuffer(64);
            for (int j = 0; j < 10; j++)
            {
                buffer.WriteInt32(i + j);
            }
        }

        sw.Stop();
        return sw.Elapsed;
    }

    private TimeSpan BenchmarkPacketBufferRead(int iterations)
    {
        // Prepare data
        var buffers = new PacketBuffer[iterations];
        for (int i = 0; i < iterations; i++)
        {
            buffers[i] = new PacketBuffer(64);
            for (int j = 0; j < 10; j++)
            {
                buffers[i].WriteInt32(i + j);
            }
            buffers[i].Flip();
        }

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var buffer = buffers[i];
            for (int j = 0; j < 10; j++)
            {
                buffer.ReadInt32();
            }
        }

        sw.Stop();

        // Cleanup
        foreach (var buffer in buffers)
        {
            buffer.Dispose();
        }

        return sw.Elapsed;
    }

    private TimeSpan BenchmarkPacketBufferComplex(int iterations)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            using var buffer = new PacketBuffer(256);

            // Write
            buffer.WriteInt32(i);
            buffer.WriteInt64(i * 1000L);
            buffer.WriteString("Test");
            buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5 });

            // Read
            buffer.Flip();
            buffer.ReadInt32();
            buffer.ReadInt64();
            buffer.ReadString();
            buffer.ReadBytes(5);
        }

        sw.Stop();
        return sw.Elapsed;
    }

    private TimeSpan BenchmarkPacketBufferMemory(int iterations)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            using var buffer = new PacketBuffer(64);
            buffer.WriteInt32(i);
            buffer.WriteInt64(i * 1000L);
            buffer.WriteString("Memory Test");
        }

        sw.Stop();
        return sw.Elapsed;
    }

    #endregion

    #region PooledBuffer Helper Methods

    private void AppendInt32(PooledBuffer buffer, int value)
    {
        var bytes = new byte[4];
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
        bytes[2] = (byte)(value >> 16);
        bytes[3] = (byte)(value >> 24);
        buffer.Append(bytes);
    }

    private void AppendInt64(PooledBuffer buffer, long value)
    {
        var bytes = new byte[8];
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
        bytes[2] = (byte)(value >> 16);
        bytes[3] = (byte)(value >> 24);
        bytes[4] = (byte)(value >> 32);
        bytes[5] = (byte)(value >> 40);
        bytes[6] = (byte)(value >> 48);
        bytes[7] = (byte)(value >> 56);
        buffer.Append(bytes);
    }

    private void AppendString(PooledBuffer buffer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        buffer.Append((byte)bytes.Length);
        buffer.Append(bytes);
    }

    private void AppendBytes(PooledBuffer buffer, byte[] value)
    {
        buffer.Append(value);
    }

    private int ReadInt32(PooledBuffer buffer, int offset)
    {
        var span = buffer.AsSpan(offset, 4);
        return span[0] | (span[1] << 8) | (span[2] << 16) | (span[3] << 24);
    }

    private long ReadInt64(PooledBuffer buffer, int offset)
    {
        var span = buffer.AsSpan(offset, 8);
        return span[0] | ((long)span[1] << 8) | ((long)span[2] << 16) | ((long)span[3] << 24) |
               ((long)span[4] << 32) | ((long)span[5] << 40) | ((long)span[6] << 48) | ((long)span[7] << 56);
    }

    private string ReadString(PooledBuffer buffer, int offset, int length)
    {
        var span = buffer.AsSpan(offset, length);
        return System.Text.Encoding.UTF8.GetString(span);
    }

    #endregion
}
