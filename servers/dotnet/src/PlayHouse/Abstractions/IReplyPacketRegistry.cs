namespace PlayHouse.Abstractions;

/// <summary>
/// Registry for tracking reply packets for automatic disposal.
/// </summary>
internal interface IReplyPacketRegistry
{
    /// <summary>
    /// Registers a reply packet for disposal after callback completion.
    /// </summary>
    void RegisterReplyForDisposal(IDisposable packet);
}
