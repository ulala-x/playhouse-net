#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Manages stage instances within the PlayHouse framework.
/// </summary>
/// <remarks>
/// StagePool provides thread-safe stage management using ConcurrentDictionary.
/// It generates unique stage IDs and maintains the lifecycle of all stages.
/// </remarks>
internal sealed class StagePool
{
    private readonly ConcurrentDictionary<int, StageContext> _stages = new();
    private int _stageIdCounter = 0;
    private readonly ILogger<StagePool> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StagePool"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public StagePool(ILogger<StagePool> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates a unique stage identifier.
    /// </summary>
    /// <returns>A unique stage ID.</returns>
    /// <remarks>
    /// Uses Interlocked.Increment for thread-safe ID generation.
    /// Stage IDs start from 1 and increment monotonically.
    /// </remarks>
    public int GenerateStageId()
    {
        return Interlocked.Increment(ref _stageIdCounter);
    }

    /// <summary>
    /// Adds a stage to the pool.
    /// </summary>
    /// <param name="context">The stage context to add.</param>
    /// <returns>True if the stage was added; false if a stage with the same ID already exists.</returns>
    public bool AddStage(StageContext context)
    {
        if (_stages.TryAdd(context.StageId, context))
        {
            _logger.LogInformation("Stage {StageId} ({StageType}) added to pool",
                context.StageId, context.StageType);
            return true;
        }

        _logger.LogWarning("Stage {StageId} already exists in pool", context.StageId);
        return false;
    }

    /// <summary>
    /// Removes a stage from the pool.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <returns>The removed stage context, or null if not found.</returns>
    public StageContext? RemoveStage(int stageId)
    {
        if (_stages.TryRemove(stageId, out var context))
        {
            _logger.LogInformation("Stage {StageId} ({StageType}) removed from pool",
                context.StageId, context.StageType);
            return context;
        }

        _logger.LogWarning("Stage {StageId} not found in pool for removal", stageId);
        return null;
    }

    /// <summary>
    /// Gets a stage from the pool.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <returns>The stage context, or null if not found.</returns>
    public StageContext? GetStage(int stageId)
    {
        _stages.TryGetValue(stageId, out var context);
        return context;
    }

    /// <summary>
    /// Checks if a stage exists in the pool.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <returns>True if the stage exists; otherwise, false.</returns>
    public bool HasStage(int stageId)
    {
        return _stages.ContainsKey(stageId);
    }

    /// <summary>
    /// Gets the number of stages in the pool.
    /// </summary>
    public int GetStageCount()
    {
        return _stages.Count;
    }

    /// <summary>
    /// Gets all stages in the pool.
    /// </summary>
    /// <returns>A collection of all stage contexts.</returns>
    public IEnumerable<StageContext> GetAllStages()
    {
        return _stages.Values.ToList();
    }

    /// <summary>
    /// Gets all stages of a specific type.
    /// </summary>
    /// <param name="stageType">The stage type name.</param>
    /// <returns>A collection of matching stage contexts.</returns>
    public IEnumerable<StageContext> GetStagesByType(string stageType)
    {
        return _stages.Values
            .Where(s => s.StageType.Equals(stageType, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Gets stage statistics for monitoring.
    /// </summary>
    /// <returns>A dictionary of stage statistics.</returns>
    public Dictionary<string, object> GetStatistics()
    {
        var stages = _stages.Values.ToList();

        return new Dictionary<string, object>
        {
            ["total_stages"] = stages.Count,
            ["total_actors"] = stages.Sum(s => s.ActorPool.Count),
            ["stages_by_type"] = stages
                .GroupBy(s => s.StageType)
                .ToDictionary(g => g.Key, g => g.Count()),
            ["queue_depths"] = stages
                .ToDictionary(s => s.StageId, s => s.QueueDepth)
        };
    }

    /// <summary>
    /// Disposes all stages in the pool.
    /// </summary>
    public async System.Threading.Tasks.Task DisposeAllAsync()
    {
        var stages = _stages.Values.ToList();
        _stages.Clear();

        foreach (var stage in stages)
        {
            try
            {
                await stage.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing stage {StageId} ({StageType})",
                    stage.StageId, stage.StageType);
            }
        }

        _logger.LogInformation("Disposed {Count} stages", stages.Count);
    }
}
