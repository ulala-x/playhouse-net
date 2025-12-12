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
    /// Kairos 패턴: 모든 메시지가 이 단일 진입점으로 전달됩니다.
    /// </summary>
    /// <param name="message">전달할 메시지 (RouteMessage, TimerMessage, AsyncMessage, DestroyMessage)</param>
    void OnPost(PlayMessage message);
}
