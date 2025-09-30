using System.Collections.Generic;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.UtilityAI;

public class GatherWoodGoal : IUtilityGoal
{
    public string Name => "Gather Wood";
    
    private const int TARGET_STICKS = 12;
    
    public float CalculateUtility(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var npcData))
            return 0f;
        
        int currentSticks = npcData.Resources.GetValueOrDefault(TargetType.Stick, 0);
        
        // Already have enough sticks
        if (currentSticks >= TARGET_STICKS)
            return 0f;
        
        // Utility decreases as we get closer to target
        float shortage = (TARGET_STICKS - currentSticks) / (float)TARGET_STICKS;
        return shortage * 0.5f; // Max utility of 0.5 (lower priority than urgent needs like hunger)
    }
    
    public State GetGoalState(Entity agent)
    {
        return new State(new Dictionary<string, object> 
        { 
            { FactKeys.AgentCount(TargetType.Stick), TARGET_STICKS } 
        });
    }
    
    public bool IsSatisfied(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var npcData))
            return false;
        
        int currentSticks = npcData.Resources.GetValueOrDefault(TargetType.Stick, 0);
        return currentSticks >= TARGET_STICKS;
    }
}
