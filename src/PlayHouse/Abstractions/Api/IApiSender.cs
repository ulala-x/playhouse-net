#nullable enable

namespace PlayHouse.Abstractions.Api;

/// <summary>
/// Provides functionality for API server handlers to send messages and manage stages.
/// </summary>
/// <remarks>
/// IApiSender extends ISender with API-specific operations:
/// - Stage creation and management
/// - Client information access
/// - Authentication context
///
/// This interface is injected into API handler methods.
/// </remarks>
public interface IApiSender : ISender
{
    /// <summary>
    /// Gets or sets the account ID for the current request.
    /// </summary>
    string AccountId { get; set; }

    /// <summary>
    /// Gets the session server NID from the current request.
    /// </summary>
    string SessionNid { get; }

    /// <summary>
    /// Gets the session ID from the current request.
    /// </summary>
    long Sid { get; }

    #region Stage Creation

    /// <summary>
    /// Creates a new stage on a Play server.
    /// </summary>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create.</param>
    /// <param name="stageId">ID for the new stage.</param>
    /// <param name="packet">Creation payload packet.</param>
    /// <returns>Result containing the error code and create response.</returns>
    Task<CreateStageResult> CreateStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket packet);

    /// <summary>
    /// Gets an existing stage or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create if needed.</param>
    /// <param name="stageId">ID for the stage.</param>
    /// <param name="createPacket">Creation payload packet (used if stage doesn't exist).</param>
    /// <param name="joinPacket">Join payload packet.</param>
    /// <returns>Result containing whether stage was created and the response.</returns>
    Task<GetOrCreateStageResult> GetOrCreateStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket createPacket,
        IPacket joinPacket);

    /// <summary>
    /// Joins an existing stage.
    /// </summary>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageId">ID of the stage to join.</param>
    /// <param name="packet">Join payload packet.</param>
    /// <returns>Result containing the error code and join response.</returns>
    Task<JoinStageResult> JoinStage(
        string playNid,
        long stageId,
        IPacket packet);

    /// <summary>
    /// Creates and joins a stage in one operation.
    /// </summary>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create if needed.</param>
    /// <param name="stageId">ID for the stage.</param>
    /// <param name="createPacket">Creation payload packet.</param>
    /// <param name="joinPacket">Join payload packet.</param>
    /// <returns>Result containing create and join responses.</returns>
    Task<CreateJoinStageResult> CreateJoinStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket createPacket,
        IPacket joinPacket);

    #endregion

    #region Client Communication

    /// <summary>
    /// Sends a packet to the client via session server.
    /// </summary>
    /// <param name="packet">Packet to send to the client.</param>
    void SendToClient(IPacket packet);

    /// <summary>
    /// Sends a packet to a specific client.
    /// </summary>
    /// <param name="sessionNid">Session server NID.</param>
    /// <param name="sid">Session ID.</param>
    /// <param name="packet">Packet to send.</param>
    void SendToClient(string sessionNid, long sid, IPacket packet);

    #endregion
}
