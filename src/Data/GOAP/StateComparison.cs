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
            //If the current state does not have the fact, we need to penalize it.
            if (!currentState.Facts.TryGetValue(goalFact.Key, out var currentValue))
            {
                // Missing goal facts contribute to heuristic
                if (goalFact.Key.EndsWith("Count") && goalFact.Value is int targetGoalCount)
                {
                    heuristic += targetGoalCount; // Distance to goal count
                }
                else
                {
                    heuristic += 1f; // Missing boolean facts
                }
                continue;
            }

            if (goalFact.Key.EndsWith("Count") && goalFact.Value is int targetGoalCount2 && currentValue is int currentCount)
            {
                // For count facts, only penalize if we have LESS than needed (since we use >= comparison)
                heuristic += Math.Max(0, targetGoalCount2 - currentCount);
            }
            else if (!currentValue.Equals(goalFact.Value))
            {
                heuristic += 1f; // Boolean fact mismatch
            }
        }

        // Special case: If goal is Near_Campfire but no campfire exists, add cost for gathering sticks
        //Senior Dev: THIS IS AWFUL! WHY ARE WE HARDCODING THIS? WE NEED TO REFACTOR THIS!
        //Me: I have no idea how to avoid hardcoding this yet. We might need to change how goals clarify their requirements a bit more...
        if (goalState.Facts.ContainsKey("Near_Campfire") && goalState.Facts["Near_Campfire"] is true)
        {
            bool hasCampfire = currentState.Facts.TryGetValue("World_Has_Campfire", out var hasCampfireValue) && hasCampfireValue is true;
            if (!hasCampfire)
            {
                // Need to build campfire, which requires 16 sticks
                const int STICKS_NEEDED = 16;
                int currentSticks = currentState.Facts.TryGetValue("Agent_Stick_Count", out var sticksObj) && sticksObj is int sticks ? sticks : 0;
                int sticksToGather = Math.Max(0, STICKS_NEEDED - currentSticks);
                
                // Each stick requires actions (goto tree, chop, pickup), estimate ~3 actions per 4 sticks
                // Plus the build action
                heuristic += sticksToGather * 0.75f + 1f;
            }
        }

        return heuristic;
    }
}