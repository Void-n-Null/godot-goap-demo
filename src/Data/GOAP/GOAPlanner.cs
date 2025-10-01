using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Game.Data.GOAP;

namespace Game.Data.GOAP;

[AttributeUsage(AttributeTargets.Class)]
public class StepFactoryAttribute : Attribute { }

public static class GOAPlanner
{
    private static List<IStepFactory> _cachedFactories;

    private static List<IStepFactory> GetStepFactories()
    {
        if (_cachedFactories == null)
        {
            // Discover factories reflectively only once
            var assembly = Assembly.GetExecutingAssembly();
            _cachedFactories = assembly.GetTypes()
                .Where(t => typeof(IStepFactory).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .Select(t => (IStepFactory)Activator.CreateInstance(t)!)
                .ToList();
            // Step factories cached
        }
        return _cachedFactories;
    }

    public static Plan Plan(State initialState, State goalState)
    {
        GD.Print("Starting GOAP planning...");

        var factories = GetStepFactories();

        // Generate initial steps
        var allSteps = GenerateStepsForState(initialState, factories);

        // Use priority queue for A* style planning with heuristic
        var openSet = new List<(State state, List<Step> path, double gCost, float fScore)>();
        try
        {
            var initialHeuristic = CalculateHeuristic(initialState, goalState);
            openSet.Add((initialState.Clone(), new List<Step>(), 0.0, initialHeuristic));
        }
        catch
        {
            // Silent fail for initial setup
            return null;
        }

        var visited = new HashSet<string>();
        int enqueuedCount = 0;
        int dequeuedCount = 0;

        while (openSet.Count > 0)
        {
            // Get state with lowest f-score (g + h)
            openSet.Sort((a, b) => a.fScore.CompareTo(b.fScore));
            var (currentState, path, gCost, _) = openSet[0];
            openSet.RemoveAt(0);
            dequeuedCount++;

            try
            {
                var stateHash = string.Join(",", currentState.Facts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
                if (visited.Contains(stateHash)) continue;
                visited.Add(stateHash);
            }
            catch
            {
                // Silent continue on hash error
                continue;
            }

            try
            {
                if (currentState.Satisfies(goalState))
                {
                    // Minimal summary log
                    GD.Print($"Plan found: {path.Count} steps, total cost {gCost:F1}");
                    return new Plan(path, initialState);
                }
            }
            catch
            {
                // Silent continue on satisfaction check
                continue;
            }

            // More aggressive depth limiting
            if (path.Count > 50)
            {
                continue;
            }

            // Use all steps generated from initial state - no regeneration
            var applicableSteps = allSteps.Where(s => s.CanRun(currentState)).ToList();

            foreach (var step in applicableSteps)
            {
                try
                {
                    var newFacts = new Dictionary<string, object>(currentState.Facts);
                    foreach (var kvp in step.Effects)
                    {
                        object newValue;
                        if (kvp.Value is Func<State, object> func)
                        {
                            newValue = func(currentState);
                        }
                        else
                        {
                            newValue = kvp.Value;
                        }
                        newFacts[kvp.Key] = newValue;
                    }
                    var newState = new State(newFacts);
                    var newPath = new List<Step>(path) { step };

                    // Calculate f-score for A* planning (f = g + h)
                    try
                    {
                        double stepCost = step.GetCost(currentState);
                        double newGCost = gCost + stepCost;
                        float heuristic = CalculateHeuristic(newState, goalState);
                        float fScore = (float)newGCost + heuristic;
                        openSet.Add((newState, newPath, newGCost, fScore));
                    }
                    catch
                    {
                        // Silent continue on heuristic error
                        continue;
                    }
                    enqueuedCount++;
                }
                catch
                {
                    // Silent continue on effects application
                    continue;
                }

                // Limit open set size more aggressively
                if (openSet.Count > 1000)
                {
                    // Keep only the best candidates
                    openSet.Sort((a, b) => a.fScore.CompareTo(b.fScore));
                    openSet.RemoveRange(500, openSet.Count - 500);
                }
            }
        }

        return null; // No plan found
    }

    private static float CalculateHeuristic(State currentState, State goalState)
    {
        float heuristic = 0f;

        foreach (var goalFact in goalState.Facts)
        {
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

    private static List<Step> GenerateStepsForState(State state, List<IStepFactory> factories)
    {
        var allSteps = new List<Step>();
        foreach (var factory in factories)
        {
            try
            {
                var steps = factory.CreateSteps(state);
                allSteps.AddRange(steps);
            }
            catch (Exception e)
            {
                GD.PushError($"Error in step factory {factory.GetType().Name}: {e.Message}");
                GD.PushError($"Stack trace: {e.StackTrace}");
            }
        }
        return allSteps;
    }

    public static async Task<Plan> PlanAsync(State initialState, State goalState)
    {
        return await Task.Run(() => Plan(initialState, goalState));
    }
}
