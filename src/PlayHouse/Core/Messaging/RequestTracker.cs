#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Messaging;

/// <summary>
/// 대기 중인 요청 추적기.
/// </summary>
/// <remarks>
/// 요청-응답 패턴에서 대기 중인 요청을 추적하고 타임아웃을 관리합니다.
/// </remarks>
public sealed class RequestTracker : IDisposable
{
    private readonly ConcurrentDictionary<ushort, PendingRequest> _pendingRequests = new();
    private readonly TimeSpan _defaultTimeout;
    private readonly ILogger? _logger;
    private readonly Timer _timeoutTimer;
    private int _nextSeq;
    private bool _disposed;

    /// <summary>
    /// 대기 중인 요청 수.
    /// </summary>
    public int PendingCount => _pendingRequests.Count;

    /// <summary>
    /// 새 RequestTracker 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="defaultTimeout">기본 타임아웃.</param>
    /// <param name="logger">로거.</param>
    public RequestTracker(TimeSpan? defaultTimeout = null, ILogger? logger = null)
    {
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        _logger = logger;

        // 1초마다 타임아웃 체크
        _timeoutTimer = new Timer(CheckTimeouts, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 새 요청을 추적합니다.
    /// </summary>
    /// <typeparam name="TResponse">응답 타입.</typeparam>
    /// <param name="timeout">타임아웃 (null이면 기본값).</param>
    /// <returns>요청 시퀀스 번호와 Task.</returns>
    public (ushort seq, Task<TResponse> task) Track<TResponse>(TimeSpan? timeout = null)
    {
        var seqInt = Interlocked.Increment(ref _nextSeq);
        if (seqInt == 0) seqInt = Interlocked.Increment(ref _nextSeq); // 0은 Push용으로 예약

        // ushort 범위로 래핑 (65535 이후 1로 돌아감)
        var seq = (ushort)((seqInt - 1) % 65535 + 1);

        var tcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingRequest(
            seq,
            typeof(TResponse),
            tcs,
            DateTimeOffset.UtcNow + (timeout ?? _defaultTimeout));

        _pendingRequests[seq] = pending;

        return (seq, tcs.Task);
    }

    /// <summary>
    /// 응답을 완료합니다.
    /// </summary>
    /// <typeparam name="TResponse">응답 타입.</typeparam>
    /// <param name="seq">요청 시퀀스 번호.</param>
    /// <param name="response">응답.</param>
    /// <returns>완료 성공 여부.</returns>
    public bool Complete<TResponse>(ushort seq, TResponse response)
    {
        if (_pendingRequests.TryRemove(seq, out var pending))
        {
            if (pending.TaskCompletionSource is TaskCompletionSource<TResponse> tcs)
            {
                tcs.TrySetResult(response);
                return true;
            }

            _logger?.LogWarning("Response type mismatch for seq {Seq}: expected {Expected}, got {Actual}",
                seq, pending.ResponseType.Name, typeof(TResponse).Name);
        }

        return false;
    }

    /// <summary>
    /// 응답을 완료합니다 (객체 버전).
    /// </summary>
    /// <param name="seq">요청 시퀀스 번호.</param>
    /// <param name="response">응답.</param>
    /// <returns>완료 성공 여부.</returns>
    public bool CompleteRaw(ushort seq, object response)
    {
        if (_pendingRequests.TryRemove(seq, out var pending))
        {
            try
            {
                // 리플렉션으로 TrySetResult 호출
                var tcsType = pending.TaskCompletionSource.GetType();
                var method = tcsType.GetMethod("TrySetResult");
                method?.Invoke(pending.TaskCompletionSource, new[] { response });
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error completing request {Seq}", seq);
            }
        }

        return false;
    }

    /// <summary>
    /// 요청을 에러로 완료합니다.
    /// </summary>
    /// <param name="seq">요청 시퀀스 번호.</param>
    /// <param name="exception">예외.</param>
    /// <returns>완료 성공 여부.</returns>
    public bool Fail(ushort seq, Exception exception)
    {
        if (_pendingRequests.TryRemove(seq, out var pending))
        {
            // 리플렉션으로 TrySetException 호출
            var tcsType = pending.TaskCompletionSource.GetType();
            var method = tcsType.GetMethod("TrySetException", new[] { typeof(Exception) });
            method?.Invoke(pending.TaskCompletionSource, new object[] { exception });
            return true;
        }

        return false;
    }

    /// <summary>
    /// 요청을 취소합니다.
    /// </summary>
    /// <param name="seq">요청 시퀀스 번호.</param>
    /// <returns>취소 성공 여부.</returns>
    public bool Cancel(ushort seq)
    {
        if (_pendingRequests.TryRemove(seq, out var pending))
        {
            // 리플렉션으로 TrySetCanceled 호출
            var tcsType = pending.TaskCompletionSource.GetType();
            var method = tcsType.GetMethod("TrySetCanceled", Type.EmptyTypes);
            method?.Invoke(pending.TaskCompletionSource, null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 모든 대기 중인 요청을 취소합니다.
    /// </summary>
    public void CancelAll()
    {
        var seqs = _pendingRequests.Keys.ToList();
        foreach (var seq in seqs)
        {
            Cancel(seq);
        }
    }

    private void CheckTimeouts(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        var timedOut = _pendingRequests
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var seq in timedOut)
        {
            if (_pendingRequests.TryRemove(seq, out var pending))
            {
                // 리플렉션으로 TrySetException 호출
                var tcsType = pending.TaskCompletionSource.GetType();
                var method = tcsType.GetMethod("TrySetException", new[] { typeof(Exception) });
                method?.Invoke(pending.TaskCompletionSource, new object[]
                {
                    new TimeoutException($"Request {seq} timed out")
                });

                _logger?.LogWarning("Request {Seq} timed out", seq);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timeoutTimer.Dispose();
        CancelAll();
    }

    private sealed class PendingRequest
    {
        public ushort Seq { get; }
        public Type ResponseType { get; }
        public object TaskCompletionSource { get; }
        public DateTimeOffset ExpiresAt { get; }

        public PendingRequest(ushort seq, Type responseType, object tcs, DateTimeOffset expiresAt)
        {
            Seq = seq;
            ResponseType = responseType;
            TaskCompletionSource = tcs;
            ExpiresAt = expiresAt;
        }
    }
}
