using Godot;

namespace Game.Universe.Scenarios;

/// <summary>
/// Base class for game scenarios that define initial world setup
/// </summary>
public abstract class Scenario
{
    /// <summary>
    /// The display name of this scenario
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// A brief description of what this scenario does
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// A description of the entities spawned in this scenario.
    /// Used for display in the UI dropdown.
    /// </summary>
    public abstract string Entities { get; }

    /// <summary>
    /// Setup the world for this scenario. Called during GameManager initialization.
    /// </summary>
    public abstract void Setup();

    /// <summary>
    /// Log scenario information
    /// </summary>
    protected void Log(string message)
    {
        GD.Print($"[Scenario: {Name}] {message}");
    }
}
