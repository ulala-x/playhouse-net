#nullable enable

using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Base interface for Stage system command handlers.
/// </summary>
/// <remarks>
/// Commands handle system messages like CreateStageReq, JoinStageReq, etc.
/// Each command is responsible for a specific operation and uses BaseStage helper methods.
/// </remarks>
internal interface IBaseStageCmd
{
    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="baseStage">The stage context.</param>
    /// <param name="packet">The incoming route packet.</param>
    Task Execute(BaseStage baseStage, RoutePacket packet);
}
