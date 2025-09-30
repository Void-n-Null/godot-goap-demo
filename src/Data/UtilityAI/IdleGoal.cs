using System.Collections.Generic;
using Game.Data;
using Game.Data.GOAP;

namespace Game.Data.UtilityAI;

public class IdleGoal : IUtilityGoal
{
    public string Name => "Idle";
    
    public float CalculateUtility(Entity agent)
    {
        // Always has low baseline utility as a fallback
        return 0.1f;
    }
    
    public State GetGoalState(Entity agent)
    {
        // Goal: Just exist (always satisfied, will trigger IdleAction)
        return new State(new Dictionary<string, object> 
        { 
            { "IsIdle", true }
        });
    }
    
    public bool IsSatisfied(Entity agent)
    {
        // Idle is always "satisfied" - it's a perpetual fallback state
        return true;
    }
}
