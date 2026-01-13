#nullable enable

using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Shared.TaskPool;

/// <summary>
/// CPU 바운드 작업을 위한 TaskPool.
/// Task.Run() 기반으로 동작하며, 동시 실행 수를 CPU 코어 수로 제한합니다.
/// </summary>
internal sealed class ComputeTaskPool : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>
    /// ComputeTaskPool을 초기화합니다.
    /// </summary>
    /// <param name="maxConcurrency">최대 동시 실행 수. 기본값은 CPU 코어 수.</param>
    /// <param name="logger">로거 (선택사항)</param>
    public ComputeTaskPool(int? maxConcurrency = null, ILogger? logger = null)
    {
        var concurrency = maxConcurrency ?? Environment.ProcessorCount;
        _semaphore = new SemaphoreSlim(concurrency, concurrency);
        _logger = logger;

        _logger?.LogInformation("ComputeTaskPool initialized (MaxConcurrency: {MaxConcurrency})", concurrency);
    }

    /// <summary>
    /// CPU 바운드 작업을 실행합니다.
    /// </summary>
    /// <param name="workItem">실행할 작업</param>
    public void Post(ITaskPoolWorkItem workItem)
    {
        if (_disposed) return;

        _ = Task.Run(async () =>
        {
            await _semaphore.WaitAsync();
            try
            {
                await workItem.ExecuteAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ComputeTaskPool] Error executing work item");
            }
            finally
            {
                _semaphore.Release();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }
}
