#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Callback delegate for handling reply packets.
/// </summary>
/// <param name="errorCode">Error code from the reply (0 = success).</param>
/// <param name="reply">The reply packet, or null if error.</param>
public delegate void ReplyCallback(ushort errorCode, IPacket? reply);

/// <summary>
/// Provides base functionality for sending packets and replies.
/// </summary>
/// <remarks>
/// ISender is the base interface for all sender types in the framework.
/// It provides methods for sending messages to API servers and Play stages,
/// as well as replying to incoming requests.
/// </remarks>
public interface ISender
{
    /// <summary>
    /// Gets the service ID of this sender.
    /// </summary>
    ushort ServiceId { get; }

    #region API Server Communication

    /// <summary>
    /// Sends a one-way packet to an API server.
    /// </summary>
    /// <param name="apiNid">Target API server NID.</param>
    /// <param name="packet">Packet to send.</param>
    void SendToApi(string apiNid, IPacket packet);

    /// <summary>
    /// Sends a request to an API server with a callback for the reply.
    /// </summary>
    /// <param name="apiNid">Target API server NID.</param>
    /// <param name="packet">Request packet.</param>
    /// <param name="replyCallback">Callback to handle the reply.</param>
    void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// Sends a request to an API server and awaits the reply.
    /// </summary>
    /// <param name="apiNid">Target API server NID.</param>
    /// <param name="packet">Request packet.</param>
    /// <returns>The reply packet.</returns>
    Task<IPacket> RequestToApi(string apiNid, IPacket packet);

    #endregion

    #region Stage Communication

    /// <summary>
    /// Sends a one-way packet to a stage on a Play server.
    /// </summary>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="packet">Packet to send.</param>
    void SendToStage(string playNid, long stageId, IPacket packet);

    /// <summary>
    /// Sends a request to a stage with a callback for the reply.
    /// </summary>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <param name="replyCallback">Callback to handle the reply.</param>
    void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// Sends a request to a stage and awaits the reply.
    /// </summary>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <returns>The reply packet.</returns>
    Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet);

    #endregion

    #region Reply

    /// <summary>
    /// Sends an error-only reply to the current request.
    /// </summary>
    /// <param name="errorCode">Error code to send.</param>
    void Reply(ushort errorCode);

    /// <summary>
    /// Sends a reply packet to the current request.
    /// </summary>
    /// <param name="reply">Reply packet.</param>
    void Reply(IPacket reply);

    #endregion
}
