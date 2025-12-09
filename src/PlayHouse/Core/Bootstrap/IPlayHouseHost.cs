#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;

namespace PlayHouse.Core.Bootstrap;

/// <summary>
/// PlayHouse 호스트 인터페이스.
/// Core 레이어에서 정의하며, Bootstrap 시스템에서 구현합니다.
/// </summary>
public interface IPlayHouseHost : IAsyncDisposable
{
    /// <summary>
    /// 호스트를 시작합니다.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 호스트를 중지합니다.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 호스트가 실행 중인지 여부
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// StageFactory 인스턴스
    /// </summary>
    StageFactory StageFactory { get; }

    /// <summary>
    /// StagePool 인스턴스
    /// </summary>
    StagePool StagePool { get; }

    /// <summary>
    /// SessionManager 인스턴스
    /// </summary>
    SessionManager SessionManager { get; }

    /// <summary>
    /// PacketDispatcher 인스턴스
    /// </summary>
    PacketDispatcher PacketDispatcher { get; }

    /// <summary>
    /// 서비스를 조회합니다.
    /// </summary>
    T GetService<T>() where T : notnull;
}
