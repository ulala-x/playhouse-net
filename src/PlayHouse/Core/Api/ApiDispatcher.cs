using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Api.Reflection;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Api;

/// <summary>
/// Stateless dispatcher for API server message handling.
/// </summary>
/// <remarks>
/// ApiDispatcher receives messages and dispatches them to registered handlers
/// via ApiReflection. Unlike PlayDispatcher, it is completely stateless -
/// each request is handled independently without any persistent state.
/// </remarks>
internal sealed class ApiDispatcher : IDisposable
{
    private readonly ServerType _serverType;
    private readonly ushort _serviceId;
    private readonly string _serverId;
    private readonly RequestCache _requestCache;
    private readonly IClientCommunicator _communicator;
    private readonly IServerInfoCenter _serverInfoCenter;
    private readonly ApiReflection _apiReflection;
    private readonly ILogger<ApiDispatcher> _logger;
    private bool _disposed;
    private int _pendingCount;
    private readonly TaskCompletionSource _drainTcs = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiDispatcher"/> class.
    /// </summary>
    /// <param name="serverType">The server type of this API server.</param>
    /// <param name="serviceId">The service ID of this API server.</param>
    /// <param name="serverId">The ServerId of this API server.</param>
    /// <param name="requestCache">The request cache for tracking pending requests.</param>
    /// <param name="communicator">The communicator for sending messages.</param>
    /// <param name="serverInfoCenter">Server information center for service discovery.</param>
    /// <param name="serviceProvider">The service provider for resolving controllers.</param>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    public ApiDispatcher(
        ServerType serverType,
        ushort serviceId,
        string serverId,
        RequestCache requestCache,
        IClientCommunicator communicator,
        IServerInfoCenter serverInfoCenter,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory)
    {
        _serverType = serverType;
        _serviceId = serviceId;
        _serverId = serverId;
        _requestCache = requestCache;
        _communicator = communicator;
        _serverInfoCenter = serverInfoCenter;
        _logger = loggerFactory.CreateLogger<ApiDispatcher>();

        var reflectionLogger = loggerFactory.CreateLogger<ApiReflection>();
        _apiReflection = new ApiReflection(serviceProvider, reflectionLogger);
    }

    /// <summary>
    /// Posts a packet for asynchronous processing using ThreadPool.
    /// </summary>
    /// <param name="packet">The packet to process.</param>
    public void Post(RoutePacket packet)
    {
        Interlocked.Increment(ref _pendingCount);
        // Use ThreadPool for efficient asynchronous processing
        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (dispatcher, routePacket) = ((ApiDispatcher, RoutePacket))state!;
            try
            {
                _ = dispatcher.DispatchAsync(routePacket);
            }
            finally
            {
                dispatcher.DecrementPending();
            }
        }, (this, packet));
    }

    /// <summary>
    /// Processes a packet asynchronously.
    /// </summary>
    /// <param name="packet">The packet to process.</param>
    private async Task DispatchAsync(RoutePacket packet)
    {
        var header = packet.Header;

        // 매번 새 ApiLink 생성 (ThreadStatic 사용 시 async/await에서 race condition 발생)
        var apiLink = new ApiLink(_communicator, _requestCache, _serverInfoCenter, _serverType, _serviceId, _serverId);
        apiLink.SetSessionContext(header);

        try
        {
            // Zero-copy contents packet
            var contentsPacket = CPacket.Of(packet.MsgId, packet.Payload);
            await _apiReflection.CallMethodAsync(contentsPacket, apiLink);
        }
        catch (PlayException e)
        {
            _logger.LogError(e, "PlayException occurred - ErrorCode: {ErrorCode}, MsgId: {MsgId}",
                e.ErrorCode, header.MsgId);

            if (header.MsgSeq > 0)
            {
                apiLink.Reply((ushort)e.ErrorCode);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected exception - msgId: {MsgId}", header.MsgId);

            if (header.MsgSeq > 0)
            {
                apiLink.Reply((ushort)ErrorCode.UncheckedContentsError);
            }
        }
        finally
        {
            apiLink.ClearSessionContext();
        }
    }

    /// <summary>
    /// Decrements the pending count and signals completion when all requests are done.
    /// </summary>
    private void DecrementPending()
    {
        if (Interlocked.Decrement(ref _pendingCount) == 0)
        {
            _drainTcs.TrySetResult();
        }
    }

    /// <summary>
    /// 진행 중인 모든 요청이 완료될 때까지 대기합니다.
    /// </summary>
    public Task DrainAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingCount == 0)
            return Task.CompletedTask;

        return _drainTcs.Task.WaitAsync(cancellationToken);
    }

    #region Metrics

    /// <summary>
    /// Gets the number of registered handlers.
    /// </summary>
    public int HandlerCount => _apiReflection.HandlerCount;

    #endregion

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
