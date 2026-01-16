#nullable enable

using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Shared.TaskPool;

/// <summary>
/// I/O 바운드 작업 (DB, 외부 API 등)을 위한 TaskPool.
/// Task.Run() 기반으로 동작하며, I/O 대기가 많으므로 동시 실행 수를 더 여유있게 설정합니다.
/// </summary>
internal sealed class IoTaskPool : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// IoTaskPool을 초기화합니다.
    /// </summary>
    /// <param name="logger">로거</param>
    /// <param name="maxConcurrency">최대 동시 실행 수. 기본값은 100.</param>
    public IoTaskPool(ILogger logger, int maxConcurrency = 100)
    {
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _logger = logger;

        _logger.LogInformation("IoTaskPool initialized (MaxConcurrency: {MaxConcurrency})", maxConcurrency);
    }

    /// <summary>
    /// I/O 바운드 작업을 실행합니다.
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
                _logger.LogError(ex, "[IoTaskPool] Error executing work item");
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
