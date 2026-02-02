#nullable enable

namespace PlayHouse.TestServer.Shared;

/// <summary>
/// Coordinates graceful shutdown requests from HTTP endpoints.
/// </summary>
public sealed class ShutdownSignal
{
    private readonly TaskCompletionSource<bool> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync()
        => _tcs.Task;

    public bool TrySignal()
        => _tcs.TrySetResult(true);
}
