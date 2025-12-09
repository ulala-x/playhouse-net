#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Tests.Integration.TestHelpers;

/// <summary>
/// Fake implementation of IActor for testing purposes.
/// Tracks method calls and provides state inspection capabilities.
/// </summary>
internal class FakeActor : IActor
{
    public IActorSender ActorSender { get; set; } = null!;

    public bool IsConnected { get; set; } = true;

    // Tracking properties
    public bool OnCreateCalled { get; private set; }
    public bool OnDestroyCalled { get; private set; }
    public bool OnAuthenticateCalled { get; private set; }
    public IPacket? LastAuthData { get; private set; }

    // Configurable behavior
    public Func<Task>? OnCreateCallback { get; set; }
    public Func<Task>? OnDestroyCallback { get; set; }
    public Func<IPacket?, Task>? OnAuthenticateCallback { get; set; }
    public bool ThrowOnAuthenticate { get; set; }

    public Task OnCreate()
    {
        OnCreateCalled = true;
        return OnCreateCallback?.Invoke() ?? Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        OnDestroyCalled = true;
        return OnDestroyCallback?.Invoke() ?? Task.CompletedTask;
    }

    public Task OnAuthenticate(IPacket? authData)
    {
        OnAuthenticateCalled = true;
        LastAuthData = authData;

        if (ThrowOnAuthenticate)
        {
            throw new InvalidOperationException("Authentication failed");
        }

        return OnAuthenticateCallback?.Invoke(authData) ?? Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public void Reset()
    {
        OnCreateCalled = false;
        OnDestroyCalled = false;
        OnAuthenticateCalled = false;
        LastAuthData = null;
        IsConnected = true;
        ThrowOnAuthenticate = false;
    }
}
