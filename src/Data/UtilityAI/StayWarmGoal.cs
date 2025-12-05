using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.UtilityAI;

public class StayWarmGoal : IUtilityGoal, IUtilityGoalCooldowns, IUtilityGoalTagInterest, IUtilityGoalWorldEventInterest
{
    private const float ComfortableTemperature = 70f;
    private const float CriticalTemperature = 30f;
    private const float WarmthRadius = 150f;

    public string Name => "Stay Warm";

    
    public float PlanFailureCooldownSeconds => 0.25f;
    public float ExecutionFailureCooldownSeconds => 0.25f;
    public float CalculateUtility(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var npcData))
            return 0f;
        
        float temp = npcData.Temperature;
        float coldFactor = Mathf.Clamp((ComfortableTemperature - temp) / ComfortableTemperature, 0f, 1f);
        if (coldFactor <= 0.01f)
            return 0f; // already warm enough

        float utility = 0.2f + coldFactor * 0.6f;

        if (temp < CriticalTemperature)
        {
            utility += 0.2f; // panic bonus when dangerously cold
        }

        if (npcData.Resources.TryGetValue(Tags.Stick, out var sticks))
        {
            utility += Mathf.Clamp(sticks / 4f, 0f, 1f) * 0.1f; // more sticks = easier to build fire
        }

        if (IsNearCampfire(agent))
        {
            // Being near a fire already satisfies the positional goal; reduced urgency keeps the plan active
            utility *= 0.4f;
        }

        return Mathf.Clamp(utility, 0f, 1f);
    }
    
    public State GetGoalState(Entity agent)
    {
        // Goal: be near a campfire so temperature can rise
        var s = new State();
        s.Set(FactKeys.NearTarget(Tags.Campfire), true);
        return s;
    }
    
    public bool IsSatisfied(Entity agent)
    {
        // Once we've reached a campfire or we're fully warmed up, this goal is satisfied.
        if (IsNearCampfire(agent))
            return true;

        if (agent.TryGetComponent<NPCData>(out var npcData) && npcData.Temperature >= ComfortableTemperature)
            return true;

        return false;
    }

    private static bool IsNearCampfire(Entity agent)
    {
        if (!agent.TryGetComponent<TransformComponent2D>(out var transform))
            return false;

        var nearbyCampfires = Universe.EntityManager.Instance.SpatialPartition
            .QueryCircle(transform.Position, WarmthRadius,
                e => e.HasTag(Tags.Campfire),
                maxResults: 1);

        return nearbyCampfires != null && nearbyCampfires.Count > 0;
    }

    public bool IsTargetTagRelevant(Tag tag)
    {
        // Only campfires should cause immediate replanning; harvested sticks are tracked via state updates.
        return tag == Tags.Campfire;
    }

    public IEnumerable<Tag> SpawnEventTags => new[] { Tags.Campfire, Tags.HeatSource };

    public IEnumerable<Tag> DespawnEventTags => [];
}
