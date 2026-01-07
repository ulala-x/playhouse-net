using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using PlayHouse.Abstractions;
using PlayHouse.Core.Api.Reflection;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play.Base;
using PlayHouse.Core.Shared;
using PlayHouse.Core.Shared.TaskPool;
using PlayHouse.Runtime.ServerMesh.Communicator;
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
    private readonly ushort _serviceId;
    private readonly string _nid;
    private readonly RequestCache _requestCache;
    private readonly IClientCommunicator _communicator;
    private readonly ApiReflection _apiReflection;
    private readonly GlobalTaskPool _taskPool;
    private readonly ILogger<ApiDispatcher>? _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiDispatcher"/> class.
    /// </summary>
    /// <param name="serviceId">The service ID of this API server.</param>
    /// <param name="nid">The NID of this API server.</param>
    /// <param name="requestCache">The request cache for tracking pending requests.</param>
    /// <param name="communicator">The communicator for sending messages.</param>
    /// <param name="serviceProvider">The service provider for resolving controllers.</param>
    /// <param name="taskPool">The shared worker task pool.</param>
    /// <param name="logger">The logger instance (optional).</param>
    public ApiDispatcher(
        ushort serviceId,
        string nid,
        RequestCache requestCache,
        IClientCommunicator communicator,
        IServiceProvider serviceProvider,
        GlobalTaskPool taskPool,
        ILogger<ApiDispatcher>? logger = null)
    {
        _serviceId = serviceId;
        _nid = nid;
        _requestCache = requestCache;
        _communicator = communicator;
        _taskPool = taskPool;
        _logger = logger;

        var reflectionLogger = logger != null
            ? LoggerFactory.Create(b => { }).CreateLogger<ApiReflection>()
            : null;
        _apiReflection = new ApiReflection(serviceProvider, reflectionLogger);
    }

    /// <summary>
    /// Posts a packet for asynchronous processing using the shared task pool.
    /// </summary>
    /// <param name="packet">The packet to process.</param>
    public void Post(RoutePacket packet)
    {
        // Use pooled work item to avoid allocations
        var workItem = ApiWorkItem.Create(this, packet);
        _taskPool.Post(workItem);
    }

    /// <summary>
    /// Internal work item for API dispatching.
    /// </summary>
    private sealed class ApiWorkItem : ITaskPoolWorkItem, IDisposable
    {
        private static readonly ObjectPool<ApiWorkItem> _pool = 
            new DefaultObjectPool<ApiWorkItem>(new DefaultPooledObjectPolicy<ApiWorkItem>());

        private ApiDispatcher _dispatcher = null!;
        private RoutePacket _packet = null!;

        public BaseStage? Stage => null;

        public static ApiWorkItem Create(ApiDispatcher dispatcher, RoutePacket packet)
        {
            var item = _pool.Get();
            item._dispatcher = dispatcher;
            item._packet = packet;
            return item;
        }

        public Task ExecuteAsync()
        {
            try
            {
                return _dispatcher.DispatchAsync(_packet);
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            _dispatcher = null!;
            _packet = null!;
            _pool.Return(this);
        }
    }

    [ThreadStatic]
    private static ApiSender? _threadLocalSender;

    /// <summary>
    /// Processes a packet asynchronously.
    /// </summary>
    /// <param name="packet">The packet to process.</param>
    internal async Task DispatchAsync(RoutePacket packet)
    {
        // Note: For S2S, we don't necessarily need to clone the packet 
        // if we process it synchronously before it's disposed.
        
        var header = packet.Header;
        
        // Optimization: Use ThreadStatic sender to avoid allocations
        var apiSender = _threadLocalSender ??= new ApiSender(_communicator, _requestCache, _serviceId, _nid);
        apiSender.SetSessionContext(header);

        try
        {
            // Zero-copy contents packet
            var contentsPacket = CPacket.Of(packet.MsgId, packet.Payload);
            await _apiReflection.CallMethodAsync(contentsPacket, apiSender);
        }
        catch (PlayException e)
        {
            _logger?.LogError(e, "PlayException occurred - ErrorCode: {ErrorCode}, MsgId: {MsgId}",
                e.ErrorCode, header.MsgId);

            if (header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)e.ErrorCode);
            }
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "Unexpected exception - msgId: {MsgId}", header.MsgId);

            if (header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)ErrorCode.UncheckedContentsError);
            }
        }
        finally
        {
            apiSender.ClearSessionContext();
        }
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
