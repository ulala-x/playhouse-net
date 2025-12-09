#nullable enable

using System;
using System.Threading.Tasks;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Implementation of IActorSender that delegates to IStageSender.
/// </summary>
/// <remarks>
/// ActorSenderImpl provides actor-specific sender functionality by wrapping
/// a StageSenderImpl and providing actor identity (AccountId, SessionId).
/// All packet sending operations are delegated to the underlying stage sender.
/// </remarks>
internal sealed class ActorSenderImpl : IActorSender
{
    private readonly long _accountId;
    private readonly long _sessionId;
    private readonly StageSenderImpl _stageSender;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorSenderImpl"/> class.
    /// </summary>
    /// <param name="accountId">The account identifier for this actor.</param>
    /// <param name="sessionId">The session identifier for this actor.</param>
    /// <param name="stageSender">The stage sender to delegate to.</param>
    public ActorSenderImpl(long accountId, long sessionId, StageSenderImpl stageSender)
    {
        _accountId = accountId;
        _sessionId = sessionId;
        _stageSender = stageSender;
    }

    /// <inheritdoc/>
    public long AccountId => _accountId;

    /// <inheritdoc/>
    public long SessionId => _sessionId;

    /// <inheritdoc/>
    public void Reply(ushort errorCode)
    {
        _stageSender.Reply(errorCode);
    }

    /// <inheritdoc/>
    public void Reply(IPacket packet)
    {
        _stageSender.Reply(packet);
    }

    /// <inheritdoc/>
    public ValueTask SendAsync(IPacket packet)
    {
        return _stageSender.SendAsync(packet);
    }
}
