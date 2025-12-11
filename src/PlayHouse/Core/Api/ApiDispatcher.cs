#nullable enable

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
    /// <param name="logger">The logger instance (optional).</param>
    public ApiDispatcher(
        ushort serviceId,
        string nid,
        RequestCache requestCache,
        IClientCommunicator communicator,
        IServiceProvider serviceProvider,
        ILogger<ApiDispatcher>? logger = null)
    {
        _serviceId = serviceId;
        _nid = nid;
        _requestCache = requestCache;
        _communicator = communicator;
        _logger = logger;

        var reflectionLogger = logger != null
            ? LoggerFactory.Create(b => { }).CreateLogger<ApiReflection>()
            : null;
        _apiReflection = new ApiReflection(serviceProvider, reflectionLogger);
    }

    /// <summary>
    /// Posts a packet for asynchronous processing.
    /// </summary>
    /// <param name="packet">The packet to process.</param>
    /// <remarks>
    /// Each packet is processed in a separate Task, making this fully stateless.
    /// The packet is owned by the caller and will be moved for processing.
    /// </remarks>
    public void Post(RuntimeRoutePacket packet)
    {
        var clonedHeader = packet.Header.Clone();
        var payloadBytes = packet.GetPayloadBytes();

        Task.Run(async () =>
        {
            var processPacket = RuntimeRoutePacket.Of(clonedHeader, payloadBytes);
            await DispatchAsync(processPacket);
        });
    }

    /// <summary>
    /// Processes a packet asynchronously.
    /// </summary>
    /// <param name="packet">The packet to process.</param>
    private async Task DispatchAsync(RuntimeRoutePacket packet)
    {
        using (packet)
        {
            var header = packet.Header;
            var apiSender = new ApiSender(_communicator, _requestCache, _serviceId, _nid);
            apiSender.SetSessionContext(header);

            try
            {
                var contentsPacket = CreateContentsPacket(packet);
                await _apiReflection.CallMethodAsync(contentsPacket, apiSender);
            }
            catch (ServiceException.NotRegisterMethod e)
            {
                _logger?.LogError(e, "Handler not registered for message: {MsgId}", header.MsgId);

                if (header.MsgSeq > 0)
                {
                    apiSender.Reply(BaseErrorCode.HandlerNotFound);
                }
            }
            catch (ServiceException.NotRegisterInstance e)
            {
                _logger?.LogError(e, "Controller instance not registered");

                if (header.MsgSeq > 0)
                {
                    apiSender.Reply(BaseErrorCode.SystemError);
                }
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Packet processing failed - msgId: {MsgId}", header.MsgId);

                if (header.MsgSeq > 0)
                {
                    apiSender.Reply(BaseErrorCode.UncheckedContentsError);
                }
            }
            finally
            {
                apiSender.ClearSessionContext();
            }
        }
    }

    /// <summary>
    /// Creates a contents packet from a route packet.
    /// </summary>
    private static IPacket CreateContentsPacket(RuntimeRoutePacket packet)
    {
        return CPacket.Of(packet.MsgId, packet.Payload.DataSpan.ToArray());
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
