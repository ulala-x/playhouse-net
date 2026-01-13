#nullable enable

using PlayHouse.Core.Shared.TaskPool;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Play;

/// <summary>
/// Interface for dispatching messages to Stages.
/// </summary>
internal interface IPlayDispatcher
{
    /// <summary>
    /// Gets the compute task pool for CPU-bound work.
    /// </summary>
    ComputeTaskPool ComputePool { get; }

    /// <summary>
    /// Gets the I/O task pool for I/O-bound work (DB, HTTP, etc.).
    /// </summary>
    IoTaskPool IoPool { get; }

    /// <summary>
    /// Kairos 패턴: 모든 메시지가 이 단일 진입점으로 전달됩니다.
    /// </summary>
    /// <param name="message">전달할 메시지 (RouteMessage, TimerMessage, AsyncMessage, DestroyMessage)</param>
    void OnPost(PlayMessage message);
}
