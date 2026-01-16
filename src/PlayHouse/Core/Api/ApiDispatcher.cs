using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Api.Reflection;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
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
    private readonly ILogger<ApiDispatcher> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiDispatcher"/> class.
    /// </summary>
    /// <param name="serviceId">The service ID of this API server.</param>
    /// <param name="nid">The NID of this API server.</param>
    /// <param name="requestCache">The request cache for tracking pending requests.</param>
    /// <param name="communicator">The communicator for sending messages.</param>
    /// <param name="serviceProvider">The service provider for resolving controllers.</param>
    /// <param name="logger">The logger instance (optional).</param>
    public ApiDispatcher(
        ushort serviceId,
        string nid,
        RequestCache requestCache,
        IClientCommunicator communicator,
        IServiceProvider serviceProvider,
        ILogger<ApiDispatcher> logger)
    {
        _serviceId = serviceId;
        _nid = nid;
        _requestCache = requestCache;
        _communicator = communicator;
        _logger = logger;

        // Create logger for ApiReflection from the same factory as ApiDispatcher
        var reflectionLogger = LoggerFactory.Create(b => { }).CreateLogger<ApiReflection>();
        _apiReflection = new ApiReflection(serviceProvider, reflectionLogger);
    }

    /// <summary>
    /// Posts a packet for asynchronous processing using ThreadPool.
    /// </summary>
    /// <param name="packet">The packet to process.</param>
    public void Post(RoutePacket packet)
    {
        // Use ThreadPool for efficient asynchronous processing
        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (dispatcher, routePacket) = ((ApiDispatcher, RoutePacket))state!;
            _ = dispatcher.DispatchAsync(routePacket);
        }, (this, packet));
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
            _logger.LogError(e, "PlayException occurred - ErrorCode: {ErrorCode}, MsgId: {MsgId}",
                e.ErrorCode, header.MsgId);

            if (header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)e.ErrorCode);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected exception - msgId: {MsgId}", header.MsgId);

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
