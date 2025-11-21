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
    
    private readonly Dictionary<Guid, float> _goalStartTimes = new();

    private static float CurrentTime => Time.GetTicksMsec() / 1000.0f;

    public State GetGoalState(Entity agent)
    {
        // Goal: Just exist (always satisfied, will trigger IdleAction)
        var s = new State();
        s.Set("IsIdle", true);
        _goalStartTimes[agent.Id] = CurrentTime;
        return s;
    }
    
    public bool IsSatisfied(Entity agent)
    {
        if (!_goalStartTimes.TryGetValue(agent.Id, out var startTime))
            return false;

        return CurrentTime - startTime >= 1.0f;
    }
}
