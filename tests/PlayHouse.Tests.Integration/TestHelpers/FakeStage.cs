#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Tests.Integration.TestHelpers;

/// <summary>
/// Fake implementation of IStage for testing purposes.
/// Tracks method calls and provides state inspection capabilities.
/// </summary>
internal class FakeStage : IStage
{
    public IStageSender StageSender { get; set; } = null!;

    // Tracking properties
    public bool OnCreateCalled { get; private set; }
    public bool OnPostCreateCalled { get; private set; }
    public List<(IActor Actor, IPacket Packet)> JoinedActors { get; } = new();
    public List<(IActor Actor, IPacket Packet)> ReceivedMessages { get; } = new();
    public List<(IActor Actor, bool IsConnected, DisconnectReason? Reason)> ConnectionChanges { get; } = new();
    public List<(IActor Actor, LeaveReason Reason)> LeftActors { get; } = new();

    // Configurable behavior
    public ushort CreateErrorCode { get; set; } = 0;
    public IPacket? CreateReply { get; set; }
    public ushort JoinErrorCode { get; set; } = 0;
    public IPacket? JoinReply { get; set; }
    public Func<IPacket, Task>? OnCreateCallback { get; set; }
    public Func<IActor, IPacket, Task>? OnJoinRoomCallback { get; set; }
    public Func<IActor, IPacket, Task>? OnDispatchCallback { get; set; }

    public Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet)
    {
        OnCreateCalled = true;
        OnCreateCallback?.Invoke(packet);
        return Task.FromResult((CreateErrorCode, CreateReply));
    }

    public Task OnPostCreate()
    {
        OnPostCreateCalled = true;
        return Task.CompletedTask;
    }

    public Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo)
    {
        JoinedActors.Add((actor, userInfo));
        OnJoinRoomCallback?.Invoke(actor, userInfo);
        return Task.FromResult((JoinErrorCode, JoinReply));
    }

    public Task OnPostJoinRoom(IActor actor)
    {
        return Task.CompletedTask;
    }

    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason)
    {
        LeftActors.Add((actor, reason));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason)
    {
        ConnectionChanges.Add((actor, isConnected, reason));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        ReceivedMessages.Add((actor, packet));
        OnDispatchCallback?.Invoke(actor, packet);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public void Reset()
    {
        OnCreateCalled = false;
        OnPostCreateCalled = false;
        JoinedActors.Clear();
        ReceivedMessages.Clear();
        ConnectionChanges.Clear();
        LeftActors.Clear();
    }
}
