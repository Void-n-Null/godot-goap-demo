using Game.Data;
using Game.Data.GOAP;

namespace Game.Data.UtilityAI;

/// <summary>
/// Represents a goal that can be evaluated for utility and executed via GOAP planning
/// </summary>
public interface IUtilityGoal
{
    /// <summary>
    /// Name of this goal for debugging
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Calculate how important this goal is right now (0-1, higher = more important)
    /// </summary>
    float CalculateUtility(Entity agent);
    
    /// <summary>
    /// Get the GOAP goal state for this utility goal
    /// </summary>
    State GetGoalState(Entity agent);
    
    /// <summary>
    /// Check if this goal is satisfied (agent can stop pursuing it)
    /// </summary>
    bool IsSatisfied(Entity agent);
}
