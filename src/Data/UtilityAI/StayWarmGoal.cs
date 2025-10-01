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
            var nearbyCampfires = Universe.EntityManager.Instance
                .QueryByComponent<TargetComponent>(transform.Position, WARMTH_RADIUS)
                .Where(e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Campfire)
                .Any();
            
            // If near a campfire, warmth is not a priority
            if (nearbyCampfires)
                return 0f;
        }
        
        // Warmth need increases over time (for demo - in reality you'd track temperature)
        // For now, give it moderate priority if agent has enough resources
        // This makes it a long-term goal - gather wood for campfire
        
        int currentSticks = npcData.Resources.TryGetValue(TargetType.Stick, out var sticks) ? sticks : 0;
        
        // If cold (no campfire nearby) and can afford to build one, prioritize it
        // Lower priority than immediate needs like hunger
        if (currentSticks >= 16)
        {
            return 0.4f; // Build campfire if we have materials
        }
        else if (currentSticks >= 8)
        {
            return 0.3f; // Gathering, getting close
        }
        else
        {
            return 0.2f; // Start gathering for future warmth
        }
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
        var nearbyCampfires = Universe.EntityManager.Instance
            .QueryByComponent<TargetComponent>(transform.Position, WARMTH_RADIUS)
            .Where(e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Campfire)
            .Any();
        
        return nearbyCampfires;
    }
}
