#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Provides base functionality for sending packets and replies.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Sends a reply with only an error code, typically used for simple acknowledgments.
    /// </summary>
    /// <param name="errorCode">The error code to send in the reply.</param>
    /// <remarks>
    /// This method should only be called in response to a request packet (MsgSeq > 0).
    /// </remarks>
    void Reply(ushort errorCode);

    /// <summary>
    /// Sends a reply packet, typically in response to a request.
    /// </summary>
    /// <param name="packet">The packet to send as a reply.</param>
    /// <remarks>
    /// The packet's MsgSeq should match the original request's MsgSeq.
    /// </remarks>
    void Reply(IPacket packet);

    /// <summary>
    /// Asynchronously sends a packet without expecting a reply.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    ValueTask SendAsync(IPacket packet);
}
