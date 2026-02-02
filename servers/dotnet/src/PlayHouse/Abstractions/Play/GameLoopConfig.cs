#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Configuration for the high-resolution game loop timer.
/// </summary>
public sealed class GameLoopConfig
{
    /// <summary>
    /// Fixed timestep for each game loop tick.
    /// Default is 50ms (20 Hz). Valid range: 1ms ~ 1000ms.
    /// </summary>
    public TimeSpan FixedTimestep { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Maximum accumulator cap to prevent Spiral of Death.
    /// When accumulated time exceeds this value, excess ticks are discarded.
    /// Default is 5 × FixedTimestep.
    /// </summary>
    public TimeSpan? MaxAccumulatorCap { get; init; }

    /// <summary>
    /// Gets the effective max accumulator cap (defaults to 5 × FixedTimestep if not set).
    /// Always clamped to at least FixedTimestep to prevent silent tick starvation.
    /// </summary>
    internal TimeSpan EffectiveMaxAccumulatorCap
    {
        get
        {
            var cap = MaxAccumulatorCap ?? TimeSpan.FromTicks(FixedTimestep.Ticks * 5);
            return cap < FixedTimestep ? FixedTimestep : cap;
        }
    }
}
