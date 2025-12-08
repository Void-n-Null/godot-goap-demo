using System;
using System.Collections.Generic;
using Game.Data.Components;

namespace Game.Data.GOAP;

public static class StateComparison
{
    public const float IMPLICIT_GOAL_WEIGHT = 0.75f;

    public static float CalculateStateComparisonHeuristic(State currentState, State goalState, State implicitGoals = null)
    {
        float h = 0f;

        // Get direct access to avoid enumerator overhead (was 45% of this method's time)
        var (goalFacts, goalActiveIds) = goalState.GetDirectAccess();
        var (currentFacts, currentActiveIds) = currentState.GetDirectAccess();

        // 1. Process explicit goals, merging any overlapping implicit requirements
        for (int i = 0; i < goalActiveIds.Count; i++)
        {
            int key = goalActiveIds[i];
            FactValue effectiveTarget = goalFacts[key];
            
            if (implicitGoals != null && implicitGoals.TryGet(key, out var implicitVal))
            {
                effectiveTarget = ResolveStricterRequirement(goalFacts[key], implicitVal);
            }

            h += CalculateCostDirect(currentFacts, currentActiveIds, key, effectiveTarget);
        }

        // 2. Process remaining implicit goals (weighted) - skip if already in explicit goals
        if (implicitGoals != null)
        {
            var (implicitFacts, implicitActiveIds) = implicitGoals.GetDirectAccess();
            
            for (int i = 0; i < implicitActiveIds.Count; i++)
            {
                int key = implicitActiveIds[i];
                
                // Skip if already processed as explicit goal (binary search on sorted list)
                if (goalActiveIds.BinarySearch(key) >= 0)
                    continue;

                float cost = CalculateCostDirect(currentFacts, currentActiveIds, key, implicitFacts[key]);
                h += cost * IMPLICIT_GOAL_WEIGHT;
            }
        }

        return h;
    }

    /// <summary>
    /// Determines which requirement is stricter when explicit and implicit goals overlap.
    /// </summary>
    private static FactValue ResolveStricterRequirement(FactValue goalVal, FactValue implicitVal)
    {
        // Numeric requirements: take the higher magnitude
        if (goalVal.Type == FactType.Int && implicitVal.Type == FactType.Int)
        {
            return implicitVal.IntValue > goalVal.IntValue ? implicitVal : goalVal;
        }

        if (goalVal.Type == FactType.Float && implicitVal.Type == FactType.Float)
        {
            return implicitVal.FloatValue > goalVal.FloatValue ? implicitVal : goalVal;
        }

        // Booleans/enums: prefer the explicit goal (end state)
        return goalVal;
    }

    /// <summary>
    /// Direct cost calculation bypassing TryGet/HasFact overhead.
    /// Uses binary search on sorted ActiveIds for existence check.
    /// </summary>
    private static float CalculateCostDirect(FactValue[] currentFacts, List<int> currentActiveIds, int key, FactValue targetVal)
    {
        // Binary search on sorted list - O(log n) for ~10-15 facts = 3-4 comparisons
        int idx = currentActiveIds.BinarySearch(key);
        if (idx >= 0)
        {
            var currentVal = currentFacts[key];
            
            if (targetVal.Type == FactType.Int && currentVal.Type == FactType.Int)
                return Math.Max(0, targetVal.IntValue - currentVal.IntValue);

            if (targetVal.Type == FactType.Float && currentVal.Type == FactType.Float)
                return Math.Max(0f, targetVal.FloatValue - currentVal.FloatValue);

            // Boolean/enum mismatch counts as 1
            return currentVal.IntValue != targetVal.IntValue || currentVal.Type != targetVal.Type ? 1f : 0f;
        }

        // Missing facts: assume zero for numerics, flat cost for others
        return targetVal.Type == FactType.Int ? targetVal.IntValue :
               targetVal.Type == FactType.Float ? targetVal.FloatValue : 1f;
    }

    private static float CalculateCost(State current, int key, FactValue targetVal)
    {
        if (current.TryGet(key, out var currentVal))
        {
            if (targetVal.Type == FactType.Int && currentVal.Type == FactType.Int)
                return Math.Max(0, targetVal.IntValue - currentVal.IntValue);

            if (targetVal.Type == FactType.Float && currentVal.Type == FactType.Float)
                return Math.Max(0f, targetVal.FloatValue - currentVal.FloatValue);

            // Boolean/enum mismatch counts as 1
            return !currentVal.Equals(targetVal) ? 1f : 0f;
        }

        // Missing facts: assume zero for numerics, flat cost for others
        if (targetVal.Type == FactType.Int)
            return targetVal.IntValue;

        if (targetVal.Type == FactType.Float)
            return targetVal.FloatValue;

        return 1f;
    }
}
