#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Tests.Integration.TestHelpers;

/// <summary>
/// Fake implementation of IStageSender for testing purposes.
/// Records all method calls for verification in tests.
/// </summary>
internal class FakeStageSender : IStageSender
{
    public int StageId { get; init; }
    public string StageType { get; init; } = "FakeStage";

    // Tracking collections
    public List<IPacket> SentPackets { get; } = new();
    public List<IPacket> BroadcastedPackets { get; } = new();
    public List<(IPacket Packet, Func<IActor, bool> Filter)> FilteredBroadcasts { get; } = new();
    public List<(int TargetStageId, IPacket Packet)> StageMessages { get; } = new();
    public List<(ushort ErrorCode, IPacket? Reply)> Replies { get; } = new();
    public List<(TimeSpan InitialDelay, TimeSpan Period, Func<Task> Callback)> RepeatTimers { get; } = new();
    public List<(TimeSpan InitialDelay, TimeSpan Period, int Count, Func<Task> Callback)> CountTimers { get; } = new();
    public List<long> CancelledTimers { get; } = new();
    public bool StageCloseCalled { get; private set; }

    private long _nextTimerId = 1;

    public ValueTask SendAsync(IPacket packet)
    {
        SentPackets.Add(packet);
        return ValueTask.CompletedTask;
    }

    public void Reply(ushort errorCode)
    {
        Replies.Add((errorCode, null));
    }

    public void Reply(IPacket packet)
    {
        Replies.Add((0, packet));
    }

    public ValueTask SendToStageAsync(int targetStageId, IPacket packet)
    {
        StageMessages.Add((targetStageId, packet));
        return ValueTask.CompletedTask;
    }

    public ValueTask BroadcastAsync(IPacket packet)
    {
        BroadcastedPackets.Add(packet);
        return ValueTask.CompletedTask;
    }

    public ValueTask BroadcastAsync(IPacket packet, Func<IActor, bool> filter)
    {
        FilteredBroadcasts.Add((packet, filter));
        return ValueTask.CompletedTask;
    }

    public long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback)
    {
        RepeatTimers.Add((initialDelay, period, callback));
        return _nextTimerId++;
    }

    public long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback)
    {
        CountTimers.Add((initialDelay, period, count, callback));
        return _nextTimerId++;
    }

    public void CancelTimer(long timerId)
    {
        CancelledTimers.Add(timerId);
    }

    public bool HasTimer(long timerId)
    {
        return !CancelledTimers.Contains(timerId);
    }

    public void CloseStage()
    {
        StageCloseCalled = true;
    }

    public void AsyncBlock(Func<Task<object?>> preCallback, Func<object?, Task>? postCallback = null)
    {
        // For testing, execute synchronously
        var result = preCallback().GetAwaiter().GetResult();
        postCallback?.Invoke(result).GetAwaiter().GetResult();
    }

    public void Reset()
    {
        SentPackets.Clear();
        BroadcastedPackets.Clear();
        FilteredBroadcasts.Clear();
        StageMessages.Clear();
        Replies.Clear();
        RepeatTimers.Clear();
        CountTimers.Clear();
        CancelledTimers.Clear();
        StageCloseCalled = false;
        _nextTimerId = 1;
    }
}
