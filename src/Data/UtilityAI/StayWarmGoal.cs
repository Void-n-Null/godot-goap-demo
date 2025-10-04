using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.UtilityAI;

public class StayWarmGoal : IUtilityGoal
{
    public string Name => "Stay Warm";
    
    public float CalculateUtility(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var npcData))
            return 0f;
        
        // Check if agent is already near a campfire (heat source)
        if (agent.TryGetComponent<TransformComponent2D>(out var transform))
        {
            const float WARMTH_RADIUS = 150f;

            // Use predicate in spatial query to avoid LINQ allocations
            var nearbyCampfires = Universe.EntityManager.Instance.SpatialPartition
                .QueryCircle(transform.Position, WARMTH_RADIUS,
                    e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Campfire,
                    maxResults: 1);

            // If near a campfire, warmth is not a priority
            if (nearbyCampfires != null && nearbyCampfires.Count > 0)
                return 0f;
        }
        
        // Warmth need increases over time (for demo - in reality you'd track temperature)
        // For now, give it moderate priority if agent has enough resources
        // This makes it a long-term goal - gather wood for campfire
        
        int currentSticks = npcData.Resources.TryGetValue(TargetType.Stick, out var sticks) ? sticks : 0;
        
        // Linear growth from 0.3 with 0 sticks to 0.5 with 16 sticks
        float utility = 0.3f + (currentSticks / 16f) * 0.2f;
        return Mathf.Clamp(utility, 0.3f, 0.5f);
    }
    
    public State GetGoalState(Entity agent)
    {
        // Goal: Be near a campfire for warmth
        return new State(new Dictionary<string, object> 
        { 
            { FactKeys.NearTarget(TargetType.Campfire), true }
        });
    }
    
    public bool IsSatisfied(Entity agent)
    {
        // Satisfied when agent is near a campfire
        if (!agent.TryGetComponent<TransformComponent2D>(out var transform))
            return false;

        const float WARMTH_RADIUS = 150f;

        // Use predicate in spatial query to avoid LINQ allocations
        var nearbyCampfires = Universe.EntityManager.Instance.SpatialPartition
            .QueryCircle(transform.Position, WARMTH_RADIUS,
                e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Campfire,
                maxResults: 1);

        return nearbyCampfires != null && nearbyCampfires.Count > 0;
    }
}
