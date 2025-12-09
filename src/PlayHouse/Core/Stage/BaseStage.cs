#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Base class for stage implementations using a lock-free event loop pattern.
/// </summary>
/// <remarks>
/// BaseStage implements a lock-free message queue using CAS (Compare-And-Swap) operations
/// and async Task execution. This ensures that:
/// 1. All messages are processed serially within the stage (single-threaded execution model)
/// 2. No locks are used, preventing deadlocks and improving performance
/// 3. Multiple threads can safely enqueue messages without blocking
///
/// The event loop pattern:
/// - Messages are enqueued into a ConcurrentQueue
/// - A single processing loop runs using CAS to ensure only one Task processes messages
/// - The loop continues until the queue is empty and no new messages arrive
/// </remarks>
public abstract class BaseStage
{
    private readonly ConcurrentQueue<RoutePacket> _msgQueue = new();
    private readonly AtomicBoolean _isProcessing = new(false);
    protected readonly IStageSender _stageSender;
    protected readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseStage"/> class.
    /// </summary>
    /// <param name="stageSender">The sender interface for this stage.</param>
    /// <param name="logger">The logger instance.</param>
    protected BaseStage(IStageSender stageSender, ILogger logger)
    {
        _stageSender = stageSender;
        _logger = logger;
    }

    /// <summary>
    /// Posts a packet to this stage's message queue for processing.
    /// </summary>
    /// <param name="routePacket">The route packet to process.</param>
    /// <remarks>
    /// This method is thread-safe and lock-free. Multiple threads can call Post concurrently.
    /// The packet will be processed serially in the order it was enqueued.
    /// </remarks>
    public void Post(RoutePacket routePacket)
    {
        _msgQueue.Enqueue(routePacket);

        // Only start processing if we're not already processing
        // This CAS operation ensures only one processing loop runs at a time
        if (_isProcessing.CompareAndSet(false, true))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessMessageLoopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in stage {StageId} message loop", _stageSender.StageId);
                }
            });
        }
    }

    /// <summary>
    /// Processes the message queue in a loop until empty.
    /// </summary>
    /// <remarks>
    /// This is the core of the lock-free event loop pattern:
    /// 1. Process all messages in the queue
    /// 2. Set processing flag to false
    /// 3. Check if new messages arrived while we were setting the flag
    /// 4. If yes, try to re-acquire the processing flag and continue
    /// 5. If we successfully re-acquire, continue processing; otherwise another thread took over
    /// </remarks>
    private async Task ProcessMessageLoopAsync()
    {
        do
        {
            // Process all available messages
            while (_msgQueue.TryDequeue(out var packet))
            {
                try
                {
                    using (packet)
                    {
                        await DispatchAsync(packet);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error dispatching packet in stage {StageId}: MsgId={MsgId}, Type={PacketType}",
                        _stageSender.StageId, packet.MsgId, packet.PacketType);
                }
            }

            // Mark that we're done processing
            _isProcessing.Set(false);

            // Double-check: if new messages arrived after we emptied the queue but before
            // we set _isProcessing to false, we need to resume processing
        } while (!_msgQueue.IsEmpty && _isProcessing.CompareAndSet(false, true));
    }

    /// <summary>
    /// Dispatches a route packet to the appropriate handler.
    /// </summary>
    /// <param name="packet">The route packet to dispatch.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    /// <remarks>
    /// Derived classes must implement this method to handle different packet types
    /// (ClientPacket, StagePacket, Timer, AsyncBlockResult).
    /// </remarks>
    protected abstract Task DispatchAsync(RoutePacket packet);

    /// <summary>
    /// Gets the current queue depth for monitoring purposes.
    /// </summary>
    public int QueueDepth => _msgQueue.Count;

    /// <summary>
    /// Gets a value indicating whether this stage is currently processing messages.
    /// </summary>
    public bool IsProcessing => _isProcessing.Value;
}
