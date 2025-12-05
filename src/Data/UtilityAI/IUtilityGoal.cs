using Game.Data;
using Game.Data.GOAP;
using System.Collections.Generic;
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

/// <summary>
/// Optional contract for goals that want to customize plan/execution failure cooldowns.
/// </summary>
public interface IUtilityGoalCooldowns
{
    /// <summary>
    /// Seconds to wait before retrying this goal after planning failed.
    /// </summary>
    float PlanFailureCooldownSeconds { get; }

    /// <summary>
    /// Seconds to wait before retrying this goal after execution failed mid-plan.
    /// </summary>
    float ExecutionFailureCooldownSeconds { get; }
}

/// <summary>
/// Optional contract for goals that care about spawn/despawn events of specific target tags
/// beyond those explicitly encoded in their goal states.
/// </summary>
public interface IUtilityGoalTagInterest
{
    /// <summary>
    /// Returns true if the given tag should trigger a re-evaluation for this goal.
    /// </summary>
    bool IsTargetTagRelevant(Tag tag);
}

/// <summary>
/// Optional contract for goals that want to receive spawn/despawn events for specific tags.
/// </summary>
public interface IUtilityGoalWorldEventInterest
{
    /// <summary>
    /// Tags whose spawns should be observed for this goal.
    /// </summary>
    IEnumerable<Tag> SpawnEventTags { get; }

    /// <summary>
    /// Tags whose despawns should be observed for this goal.
    /// Can be empty if despawns are not relevant.
    /// </summary>
    IEnumerable<Tag> DespawnEventTags { get; }
}
