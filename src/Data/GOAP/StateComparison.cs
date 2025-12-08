using System;
using Game.Data.Components;

namespace Game.Data.GOAP;

public static class StateComparison
{
    public const float IMPLICIT_GOAL_WEIGHT = 0.75f;

    public static float CalculateStateComparisonHeuristic(State currentState, State goalState, State implicitGoals = null)
    {
        float h = 0f;

        // 1. Process explicit goals, merging any overlapping implicit requirements
        foreach (var goalFact in goalState.FactsById)
        {
            FactValue effectiveTarget = goalFact.Value;
            if (implicitGoals != null && implicitGoals.TryGet(goalFact.Key, out var implicitVal))
            {
                effectiveTarget = ResolveStricterRequirement(goalFact.Value, implicitVal);
            }

            h += CalculateCost(currentState, goalFact.Key, effectiveTarget);
        }

        // 2. Process remaining implicit goals (weighted) - skip if already in explicit goals
        if (implicitGoals != null)
        {
            foreach (var implicitFact in implicitGoals.FactsById)
            {
                // Skip if already processed as explicit goal (no BitArray allocation needed)
                if (goalState.TryGet(implicitFact.Key, out _))
                    continue;

                float cost = CalculateCost(currentState, implicitFact.Key, implicitFact.Value);
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
