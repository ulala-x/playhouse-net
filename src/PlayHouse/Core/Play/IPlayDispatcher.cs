#nullable enable

using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Play;

/// <summary>
/// Interface for dispatching messages to Stages.
/// </summary>
/// <remarks>
/// PlayDispatcher routes messages to the appropriate Stage based on StageId.
/// It manages Stage lifecycle and message queuing.
/// </remarks>
internal interface IPlayDispatcher
{
    /// <summary>
    /// Posts a message to be dispatched to a Stage.
    /// </summary>
    /// <param name="packet">The packet to dispatch.</param>
    void Post(RuntimeRoutePacket packet);

    /// <summary>
    /// Posts a timer operation packet.
    /// </summary>
    /// <param name="timerPacket">Timer packet.</param>
    void PostTimer(TimerPacket timerPacket);

    /// <summary>
    /// Posts an AsyncBlock result packet.
    /// </summary>
    /// <param name="asyncPacket">AsyncBlock packet.</param>
    void PostAsyncBlock(AsyncBlockPacket asyncPacket);

    /// <summary>
    /// Posts a stage destroy request.
    /// </summary>
    /// <param name="stageId">Stage ID to destroy.</param>
    void PostDestroy(long stageId);
}
