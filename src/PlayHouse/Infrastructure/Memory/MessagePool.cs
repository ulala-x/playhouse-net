#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Infrastructure.Memory;

/// <summary>
/// 버킷별 차등 정책과 모니터링, 자동 축소 기능을 갖춘 지능형 메시지 전용 메모리 풀.
/// </summary>
public static class MessagePool
{
    private const int BUCKET_COUNT = 53;
    private static int _maxL1Capacity = 64;

    private static MessagePoolConfig _currentConfig = new();
    private static readonly int[] _maxCapacities = new int[BUCKET_COUNT];
    private static readonly int[] _warmUpCounts = new int[BUCKET_COUNT];
    private static readonly int[] _bucketSizes = new int[BUCKET_COUNT];
    
    private static readonly long[] _rentCounts = new long[BUCKET_COUNT];
    private static readonly long[] _allocationCounts = new long[BUCKET_COUNT];
    private static readonly long[] _returnRejectedCounts = new long[BUCKET_COUNT];
    private static readonly long[] _lastAccessTicks = new long[BUCKET_COUNT];

    private static readonly ConcurrentStack<byte[]>[] _globalPool = new ConcurrentStack<byte[]>[BUCKET_COUNT];

    [ThreadStatic]
    private static Stack<byte[]>?[]? _localCache;

    private static Timer? _trimTimer;

    static MessagePool()
    {
        // [CGDK10 53단계 버킷 사이즈 명시적 초기화]
        for (int i = 0; i < BUCKET_COUNT; i++)
        {
            if (i <= 7) _bucketSizes[i] = (i + 1) * 128; // 128 ~ 1024
            else if (i <= 14) _bucketSizes[i] = 1024 + (i - 7) * 1024; // 2048 ~ 8192
            else if (i <= 28) _bucketSizes[i] = 8192 + (i - 14) * 4096; // 12288 ~ 65536
            else if (i <= 43) _bucketSizes[i] = 65536 + (i - 28) * 16384; // 81920 ~ 311296
            else _bucketSizes[i] = 311296 + (i - 43) * 65536; // 376832 ~ 966656 (approx to 1MB)

            _globalPool[i] = new ConcurrentStack<byte[]>();
        }
        
        ApplyConfig(new MessagePoolConfig());
    }

    public static void ApplyConfig(MessagePoolConfig config)
    {
        _currentConfig = config;
        _maxL1Capacity = config.MaxL1Capacity;

        for (int i = 0; i < BUCKET_COUNT; i++)
        {
            int size = _bucketSizes[i];
            if (size <= 1024) { _maxCapacities[i] = config.MaxTinyCount; _warmUpCounts[i] = config.TinyWarmUpCount; }
            else if (size <= 8192) { _maxCapacities[i] = config.MaxSmallCount; _warmUpCounts[i] = config.SmallWarmUpCount; }
            else if (size <= 65536) { _maxCapacities[i] = config.MaxMediumCount; _warmUpCounts[i] = config.MediumWarmUpCount; }
            else if (size <= 311296) { _maxCapacities[i] = config.MaxLargeCount; _warmUpCounts[i] = config.LargeWarmUpCount; }
            else { _maxCapacities[i] = config.MaxHugeCount; _warmUpCounts[i] = 10; }
        }

        _trimTimer?.Dispose();
        if (_currentConfig.EnableAutoTrim)
            _trimTimer = new Timer(_ => TrimPool(), null, _currentConfig.TrimCheckInterval, _currentConfig.TrimCheckInterval);
    }

    private static void TrimPool()
    {
        long now = Stopwatch.GetTimestamp();
        long thresholdTicks = (long)(_currentConfig.IdleThreshold.TotalSeconds * Stopwatch.Frequency);

        for (int i = 0; i < BUCKET_COUNT; i++)
        {
            if (_globalPool[i].Count > _warmUpCounts[i] && (now - Volatile.Read(ref _lastAccessTicks[i])) > thresholdTicks)
            {
                int toRemove = Math.Max(10, (_globalPool[i].Count - _warmUpCounts[i]) / 5);
                for (int j = 0; j < toRemove; j++) if (_globalPool[i].TryPop(out _)) { } else break;
            }
        }
    }

    public static void WarmUp()
    {
        for (int i = 0; i < BUCKET_COUNT; i++)
        {
            for (int j = 0; j < _warmUpCounts[i]; j++)
            {
                var buffer = new byte[_bucketSizes[i]];
                buffer.AsSpan().Clear();
                _globalPool[i].Push(buffer);
            }
            Volatile.Write(ref _lastAccessTicks[i], Stopwatch.GetTimestamp());
        }
    }

    public static byte[] Rent(int size)
    {
        if (size <= 0) return Array.Empty<byte>();
        if (size > _bucketSizes[BUCKET_COUNT - 1]) return new byte[size];

        int index = GetBucketIndex(size);
        Interlocked.Increment(ref _rentCounts[index]);
        Volatile.Write(ref _lastAccessTicks[index], Stopwatch.GetTimestamp());

        _localCache ??= new Stack<byte[]>[BUCKET_COUNT];
        var localStack = _localCache[index] ??= new Stack<byte[]>(_maxL1Capacity);
        if (localStack.TryPop(out var buffer)) return buffer;
        if (_globalPool[index].TryPop(out buffer)) return buffer;

        Interlocked.Increment(ref _allocationCounts[index]);
        return new byte[_bucketSizes[index]];
    }

    public static void Return(byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0) return;
        int index = GetBucketIndex(buffer.Length);
        if (index >= BUCKET_COUNT || buffer.Length != _bucketSizes[index]) return;

        _localCache ??= new Stack<byte[]>[BUCKET_COUNT];
        var localStack = _localCache[index] ??= new Stack<byte[]>(_maxL1Capacity);
        if (localStack.Count < _maxL1Capacity) { localStack.Push(buffer); return; }
        if (_globalPool[index].Count < _maxCapacities[index]) _globalPool[index].Push(buffer);
        else Interlocked.Increment(ref _returnRejectedCounts[index]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBucketIndex(int size)
    {
        // Binary search for the correct bucket index
        int low = 0;
        int high = BUCKET_COUNT - 1;
        int index = high;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            if (_bucketSizes[mid] >= size)
            {
                index = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return index;
    }

    public static int GetBucketSize(int index) => _bucketSizes[index];

    public static void PrintStats(Action<string> logAction)
    {
        logAction("=== MessagePool Stats ===");
        for (int i = 0; i < BUCKET_COUNT; i++)
            if (Volatile.Read(ref _allocationCounts[i]) > 0 || _globalPool[i].Count > 0)
                logAction($"Bucket {i} ({_bucketSizes[i]}B): GlobalPool={_globalPool[i].Count}/{_maxCapacities[i]}, NewAllocs={_allocationCounts[i]}, Rejected={_returnRejectedCounts[i]}");
    }
}
