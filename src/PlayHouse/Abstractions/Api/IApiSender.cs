#nullable enable

namespace PlayHouse.Abstractions.Api;

/// <summary>
/// Provides functionality for API server handlers to send messages and manage stages.
/// </summary>
/// <remarks>
/// IApiSender extends ISender with API-specific operations:
/// - Stage creation and management
/// - Authentication context
///
/// This interface is injected into API handler methods.
/// </remarks>
public interface IApiSender : ISender
{
    /// <summary>
    /// Gets whether the current message is a Request (expects a reply).
    /// </summary>
    bool IsRequest { get; }

    /// <summary>
    /// Gets the originating Stage ID for the current request.
    /// </summary>
    long StageId { get; }

    /// <summary>
    /// Gets or sets the account ID for the current request.
    /// </summary>
    string AccountId { get; set; }

    /// <summary>
    /// Gets the source server NID from the current request.
    /// </summary>
    string FromNid { get; }

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
    /// <returns>Result containing whether stage was created and the response.</returns>
    Task<GetOrCreateStageResult> GetOrCreateStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket createPacket);

    #endregion
}
