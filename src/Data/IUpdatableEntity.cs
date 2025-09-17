namespace Game.Data;

/// <summary>
/// Base interface for all entities that need regular updates.
/// This interface defines the core contract for entities that participate
/// in the game update loop.
/// </summary>
public interface IUpdatableEntity
{
    /// <summary>
    /// Called every update cycle with the elapsed time since the last update.
    /// Implement this to define the entity's behavior and state changes.
    /// </summary>
    /// <param name="delta">Time elapsed since last update in seconds</param>
    void Update(double delta);
}
