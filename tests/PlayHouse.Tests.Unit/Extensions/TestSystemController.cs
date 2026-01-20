#nullable enable

using PlayHouse.Abstractions.System;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Tests.Unit.Extensions;

/// <summary>
/// 단위 테스트용 더미 SystemController 구현체.
/// </summary>
internal class TestSystemController : ISystemController
{
    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
        => Task.FromResult<IReadOnlyList<IServerInfo>>(new List<IServerInfo>());
}
