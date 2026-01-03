#nullable enable

using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Play.EventLoop;

/// <summary>
/// Stage ID를 기반으로 EventLoop를 선택하여 같은 Stage는 항상 같은 스레드에서 실행되도록 보장하는 풀.
/// </summary>
/// <remarks>
/// - CPU 코어 수만큼 EventLoop를 생성하여 효율적인 스레드 관리
/// - Stage ID의 해시를 사용하여 일관된 EventLoop 할당
/// - Dispose 시 모든 EventLoop 정리
/// </remarks>
internal sealed class StageEventLoopPool : IDisposable
{
    private readonly StageEventLoop[] _eventLoops;
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>
    /// 풀의 크기(EventLoop 개수)를 반환합니다.
    /// </summary>
    public int PoolSize => _eventLoops.Length;

    /// <summary>
    /// StageEventLoopPool의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="poolSize">생성할 EventLoop 개수. 0이면 Environment.ProcessorCount를 사용합니다.</param>
    /// <param name="loggerFactory">선택적 로거 팩토리.</param>
    public StageEventLoopPool(int poolSize = 0, ILoggerFactory? loggerFactory = null)
    {
        // poolSize가 0이면 CPU 코어 수만큼 생성
        if (poolSize <= 0)
        {
            poolSize = Environment.ProcessorCount;
        }

        _logger = loggerFactory?.CreateLogger<StageEventLoopPool>();
        _eventLoops = new StageEventLoop[poolSize];

        // EventLoop 배열 초기화
        for (var i = 0; i < poolSize; i++)
        {
            var logger = loggerFactory?.CreateLogger<StageEventLoop>();
            _eventLoops[i] = new StageEventLoop(i, logger);
        }

        _logger?.LogInformation("StageEventLoopPool initialized with {PoolSize} EventLoops", poolSize);
    }

    /// <summary>
    /// Stage ID를 기반으로 적절한 EventLoop를 선택합니다.
    /// 같은 Stage ID는 항상 같은 EventLoop를 반환하여 순차 실행을 보장합니다.
    /// </summary>
    /// <param name="stageId">Stage 식별자.</param>
    /// <returns>할당된 EventLoop.</returns>
    /// <exception cref="ObjectDisposedException">풀이 이미 Dispose된 경우.</exception>
    public StageEventLoop GetEventLoop(long stageId)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(StageEventLoopPool));
        }

        // Stage ID의 절대값을 배열 크기로 나눈 나머지로 인덱스 계산
        // 음수 ID도 올바르게 처리하기 위해 Math.Abs 사용
        var index = (int)(Math.Abs(stageId) % _eventLoops.Length);
        return _eventLoops[index];
    }

    /// <summary>
    /// 풀의 모든 EventLoop를 정리하고 스레드를 종료합니다.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger?.LogInformation("Disposing StageEventLoopPool with {PoolSize} EventLoops", _eventLoops.Length);

        // 모든 EventLoop Dispose
        foreach (var eventLoop in _eventLoops)
        {
            eventLoop.Dispose();
        }

        _logger?.LogInformation("StageEventLoopPool disposed");
    }
}
