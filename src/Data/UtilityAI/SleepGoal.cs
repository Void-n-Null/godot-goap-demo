using System;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.UtilityAI;

public class SleepGoal : IUtilityGoal
{
    private const float SleepThreshold = 70f;
    private const float CriticalSleepiness = 90f;

    public string Name => "Sleep";

    public float CalculateUtility(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var npcData))
            return 0f;

        float sleepiness = npcData.Sleepiness;
        
        // If sleepiness is very low, no need to sleep
        if (sleepiness <= 5f)
            return 0f;
        
        // Linear curve: 0 utility at 30 sleepiness, up to 1.0 at 100
        float utility = Mathf.Clamp((sleepiness - 30f) / 70f, 0f, 1f);

        if (sleepiness > CriticalSleepiness)
        {
            utility += 0.2f; // Panic bonus when exhausted
        }

        // CRITICAL: If we're above the threshold, maintain high utility until fully rested
        // This prevents the goal from being interrupted mid-sleep
        if (sleepiness > SleepThreshold)
        {
            utility = Mathf.Max(utility, 0.85f); // Keep utility high enough to stay committed
        }

        return Mathf.Clamp(utility, 0f, 1f);
    }

    public State GetGoalState(Entity agent)
    {
        // Goal: Be no longer sleepy
        var s = new State();
        s.Set("IsSleepy", false); 
        return s;
    }

    public bool IsSatisfied(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var npcData))
            return true;

        // Satisfied when sleepiness drops to near zero
        return npcData.Sleepiness <= 5f; 
    }
}

