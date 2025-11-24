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
        
        // Linear curve: 0 utility at 30 sleepiness, up to 1.0 at 100
        float utility = Mathf.Clamp((sleepiness - 30f) / 70f, 0f, 1f);

        if (sleepiness > CriticalSleepiness)
        {
            utility += 0.2f; // Panic bonus when exhausted
        }

        // If we are already sleeping (IsSleepy fact false, but sleepiness high? No wait, IsSleepy is the goal condition)
        // If we are successfully sleeping, utility should drop? 
        // Actually, UtilityGoalSelector picks the highest utility. If we are sleeping, sleepiness decreases.
        // As sleepiness decreases, utility decreases, eventually another goal takes over.

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

