#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.System;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.TestServer.Shared;

/// <summary>
/// Test Server용 SystemController 구현.
/// 서버 디스커버리를 위한 인메모리 구현을 사용합니다.
/// </summary>
public class TestSystemController : ISystemController
{
    private readonly ILogger<TestSystemController> _logger;
    private readonly InMemorySystemController _inMemory;

    public TestSystemController(ILogger<TestSystemController>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TestSystemController>.Instance;
        _inMemory = new InMemorySystemController(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// 서버 디스커버리를 위한 서버 정보 업데이트.
    /// </summary>
    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        _logger.LogDebug(
            "Updating server info: ServerId={ServerId}, Address={Address}",
            serverInfo.ServerId,
            serverInfo.Address);

        return _inMemory.UpdateServerInfoAsync(serverInfo);
    }

    /// <summary>
    /// 시스템 메시지 핸들러 등록 (선택적).
    /// </summary>
    public void Handles(ISystemHandlerRegister handlerRegister)
    {
        _logger.LogInformation("SystemController initialized");
    }
}
