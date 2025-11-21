using Game.Data.Components;
using Game.Data.GOAP;
using System;
using Godot;

namespace Game.Data.GOAP;

public static class StateComparison
{
    public static float CalculateStateComparisonHeuristic(State currentState, State goalState)
    {
        //We need to compare the current state to the goal state, and return a heuristic value.
        float heuristic = 0f;

        //For each fact in the goal state, we need to compare it to the current state.
        //Usually these goal states are pretty small, so doing this at scale should be fine... (I hope)
        foreach (var goalFact in goalState.Facts)
        {
            if (!currentState.TryGet(goalFact.Key, out var currentValue))
            {
                if (goalFact.Key.EndsWith("Count") && goalFact.Value.Type == FactType.Int)
                {
                    heuristic += goalFact.Value.IntValue;
                }
                else
                {
                    heuristic += 1f;
                }
                continue;
            }

            if (goalFact.Key.EndsWith("Count") && goalFact.Value.Type == FactType.Int && currentValue.Type == FactType.Int)
            {
                heuristic += Math.Max(0, goalFact.Value.IntValue - currentValue.IntValue);
            }
            else if (!currentValue.Equals(goalFact.Value))
            {
                heuristic += 1f;
            }
        }

        // Special case: If goal is Near_Campfire but no campfire exists, add cost for gathering sticks
        //Senior Dev: THIS IS AWFUL! WHY ARE WE HARDCODING THIS? WE NEED TO REFACTOR THIS!
        //Me: I have no idea how to avoid hardcoding this yet. We might need to change how goals clarify their requirements a bit more...
        if (goalState.TryGet(FactKeys.NearTarget(TargetType.Campfire), out var nearCampfire) && nearCampfire.BoolValue)
        {
            bool hasCampfire = currentState.TryGet(FactKeys.WorldHas(TargetType.Campfire), out var hasCampfireValue) && hasCampfireValue.BoolValue;
            if (!hasCampfire)
            {
                // Need to build campfire, which requires 16 sticks
                const int STICKS_NEEDED = 16;
                int currentSticks = currentState.TryGet(FactKeys.AgentCount(TargetType.Stick), out var sticksVal) ? sticksVal.IntValue : 0;
                int sticksToGather = Math.Max(0, STICKS_NEEDED - currentSticks);
                
                // Each stick requires actions (goto tree, chop, pickup), estimate ~3 actions per 4 sticks
                // Plus the build action
                heuristic += sticksToGather * 0.75f + 1f;
            }
        }

        return heuristic;
    }
}