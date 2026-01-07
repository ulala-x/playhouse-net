#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Infrastructure.Memory;

/// <summary>
/// 버킷별 차등 정책과 모니터링 기능을 갖춘 지능형 메시지 전용 메모리 풀.
/// 작은 버퍼는 넉넉하게, 큰 버퍼는 효율적으로 관리합니다.
/// </summary>
public static class MessagePool
{
    private const int BUCKET_COUNT = 53;
    private const int MAX_L1_CAPACITY = 64; // 스레드별 로컬 보관 최대 개수

    // 버킷별 최대 보관 가능 개수 (L2)
    private static readonly int[] _maxCapacities = new int[BUCKET_COUNT];
    
    // 모니터링 지표
    private static readonly long[] _rentCounts = new long[BUCKET_COUNT];
    private static readonly long[] _allocationCounts = new long[BUCKET_COUNT];
    private static readonly long[] _returnRejectedCounts = new long[BUCKET_COUNT];

    // L2: 전역 공유 스택
    private static readonly ConcurrentStack<byte[]>[] _globalPool = new ConcurrentStack<byte[]>[BUCKET_COUNT];

    // L1: 스레드별 로컬 캐시
    [ThreadStatic]
    private static Stack<byte[]>?[]? _localCache;

    static MessagePool()
    {
        for (int i = 0; i < BUCKET_COUNT; i++)
        {
            _globalPool[i] = new ConcurrentStack<byte[]>();
            
            // [차등 정책 설정]
            int size = GetBucketSize(i);
            if (size <= 1024) // 1KB 이하: 아주 넉넉하게 (최대 10만 개)
                _maxCapacities[i] = 100000;
            else if (size <= 8192) // 8KB 이하: 넉넉하게 (최대 2만 개)
                _maxCapacities[i] = 20000;
            else if (size <= 65536) // 64KB 이하: 적당하게 (최대 5천 개)
                _maxCapacities[i] = 5000;
            else if (size <= 262144) // 256KB 이하: 엄격하게 (최대 500 개)
                _maxCapacities[i] = 500;
            else // 1MB 이하: 최소한으로 (최대 100 개)
                _maxCapacities[i] = 100;
        }
    }

    /// <summary>
    /// 버킷별 정책에 맞춰 메모리 풀을 미리 채웁니다.
    /// </summary>
    /// <param name="scaleFactor">정책상 Min 수치에 곱할 계수 (1.0 = 표준 웜업)</param>
    public static void WarmUp(float scaleFactor = 1.0f)
    {
        for (int i = 0; i < BUCKET_COUNT; i++)
        {
            int size = GetBucketSize(i);
            
            // 버킷 크기에 따라 웜업 수치 결정
            int warmupCount;
            if (size <= 1024) warmupCount = (int)(20000 * scaleFactor);
            else if (size <= 8192) warmupCount = (int)(5000 * scaleFactor);
            else if (size <= 65536) warmupCount = (int)(500 * scaleFactor);
            else warmupCount = (int)(10 * scaleFactor);

            for (int j = 0; j < warmupCount; j++)
            {
                var buffer = new byte[size];
                buffer.AsSpan().Clear(); // 물리 메모리 커밋 강제
                _globalPool[i].Push(buffer);
            }
        }
    }

    public static byte[] Rent(int size)
    {
        if (size <= 0) return Array.Empty<byte>();
        if (size > 1048576) return new byte[size];

        int index = GetBucketIndex(size);
        Interlocked.Increment(ref _rentCounts[index]);

        // 1. L1 캐시 확인
        _localCache ??= new Stack<byte[]>[BUCKET_COUNT];
        var localStack = _localCache[index] ??= new Stack<byte[]>(MAX_L1_CAPACITY);

        if (localStack.TryPop(out var buffer)) return buffer;

        // 2. L2 전역 풀 확인
        if (_globalPool[index].TryPop(out buffer)) return buffer;

        // 3. 새로 생성 (풀 고갈 시)
        Interlocked.Increment(ref _allocationCounts[index]);
        return new byte[GetBucketSize(index)];
    }

    public static void Return(byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0) return;

        int size = buffer.Length;
        if (size > 1048576) return;

        int index = GetBucketIndex(size);
        if (buffer.Length != GetBucketSize(index)) return;

        // 1. L1 캐시 반납 시도
        _localCache ??= new Stack<byte[]>[BUCKET_COUNT];
        var localStack = _localCache[index] ??= new Stack<byte[]>(MAX_L1_CAPACITY);

        if (localStack.Count < MAX_L1_CAPACITY)
        {
            localStack.Push(buffer);
            return;
        }

        // 2. L2 전역 풀 반납 (용량 제한 체크)
        if (_globalPool[index].Count < _maxCapacities[index])
        {
            _globalPool[index].Push(buffer);
        }
        else
        {
            // 풀이 가득 참 - 반납 거부 (GC에 맡김)
            Interlocked.Increment(ref _returnRejectedCounts[index]);
        }
    }

    /// <summary>
    /// 현재 풀의 상태 지표를 출력합니다.
    /// </summary>
    public static void PrintStats(Action<string> logAction)
    {
        logAction("=== MessagePool Stats ===");
        for (int i = 0; i < BUCKET_COUNT; i++)
        {
            long allocs = Volatile.Read(ref _allocationCounts[i]);
            if (allocs > 0 || _globalPool[i].Count > 0)
            {
                logAction($"Bucket {i} ({GetBucketSize(i)}B): GlobalPool={_globalPool[i].Count}/{_maxCapacities[i]}, NewAllocs={allocs}, Rejected={_returnRejectedCounts[i]}");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBucketIndex(int size)
    {
        if (size <= 1024) return (size - 1) >> 7;
        if (size <= 8192) return 8 + ((size - 1025) >> 10);
        if (size <= 65536) return 15 + ((size - 8193) >> 12);
        if (size <= 262144) return 29 + ((size - 65537) >> 14);
        return 44 + ((size - 262145) >> 16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBucketSize(int index)
    {
        if (index <= 7) return (index + 1) * 128;
        if (index <= 14) return 1024 + (index - 7) * 1024;
        if (index <= 28) return 8192 + (index - 14) * 4096;
        if (index <= 43) return 65536 + (index - 28) * 16384;
        return 262144 + (index - 43) * 65536;
    }
}