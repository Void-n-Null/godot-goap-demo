using Godot;
using System;

namespace Game;

/// <summary>
/// Manages game time and provides tick-based updates at a specified rate.
/// </summary>
public class TimeManager
{
    /// <summary>
    /// Gets the number of ticks per second.
    /// </summary>
    public int TickRatePerSecond { get; private set; }

    /// <summary>
    /// Gets the interval between ticks in seconds.
    /// </summary>
    public double TickInterval => 1.0 / TickRatePerSecond;

    /// <summary>
    /// Event that fires on each tick.
    /// </summary>
    public event Action<double> Tick;

    private double _accumulatedTime = 0.0;

    /// <summary>
    /// Initializes a new instance of the TimeManager class.
    /// </summary>
    /// <param name="tickRatePerSecond">The number of ticks per second. Must be positive.</param>
    /// <exception cref="ArgumentException">Thrown when tickRatePerSecond is not positive.</exception>
    public TimeManager(int tickRatePerSecond)
    {
        if (tickRatePerSecond <= 0)
        {
            throw new ArgumentException("Tick rate per second must be positive.", nameof(tickRatePerSecond));
        }

        TickRatePerSecond = tickRatePerSecond;
    }

    /// <summary>
    /// Progresses time by the specified delta and triggers tick events as needed.
    /// </summary>
    /// <param name="delta">The time elapsed since the last update in seconds.</param>
    public void Progress(double delta)
    {
        _accumulatedTime += delta;
        while (_accumulatedTime >= TickInterval)
        {
            _accumulatedTime -= TickInterval;
            Tick?.Invoke(TickInterval);
        }
    }
}