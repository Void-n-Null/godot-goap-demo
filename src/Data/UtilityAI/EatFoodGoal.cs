using System.Collections.Generic;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.UtilityAI;

public class EatFoodGoal : IUtilityGoal
{
    public string Name => "Eat Food";
    
    public float CalculateUtility(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var npcData))
            return 0f;
        
        // High utility when hungry (hunger is 0-100, higher = more hungry)
        float hungerRatio = npcData.Hunger / npcData.MaxHunger;
        
        // Only care about eating when hunger > 30%
        if (hungerRatio < 0.3f)
            return 0f;
        
        // Exponential curve - becomes VERY important as hunger increases
        // At 30% hunger: ~0.2 utility
        // At 50% hunger: ~0.5 utility
        // At 80% hunger: ~0.9 utility
        // At 100% hunger: 1.0 utility (max priority!)
        return Mathf.Pow(hungerRatio, 2f);
    }
    
    public State GetGoalState(Entity agent)
    {
        // Goal: Have food consumed (hunger reduced)
        return new State(new Dictionary<string, object> 
        { 
            { "FoodConsumed", true }
        });
    }
    
    public bool IsSatisfied(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var npcData))
            return false;
        
        // Satisfied when hunger is below 20%
        return npcData.Hunger < npcData.MaxHunger * 0.2f;
    }
}
