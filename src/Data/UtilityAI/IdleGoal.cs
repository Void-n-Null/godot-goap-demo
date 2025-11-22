using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.GOAP;
using Godot;

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
        var s = new State();
        s.Set("IsIdle", true);
        return s;
    }

    public bool IsSatisfied(Entity agent)
    {
        // Never satisfied - always valid to keep idling if nothing better to do
        return false;
    }
}
