#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Tests.Integration.TestHelpers;

/// <summary>
/// Fake implementation of IActorSender for testing purposes.
/// Records all method calls for verification in tests.
/// </summary>
internal class FakeActorSender : IActorSender
{
    public long AccountId { get; init; }
    public long SessionId { get; init; }

    // Tracking collections
    public List<IPacket> SentPackets { get; } = new();
    public List<(ushort ErrorCode, IPacket? Reply)> Replies { get; } = new();

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

    public void Reset()
    {
        SentPackets.Clear();
        Replies.Clear();
    }
}
