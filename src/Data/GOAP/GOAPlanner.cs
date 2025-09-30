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
        var openSet = new List<(State state, List<Step> path, int depth, float heuristic)>();
        try
        {
            var initialHeuristic = CalculateHeuristic(initialState, goalState);
            openSet.Add((initialState.Clone(), new List<Step>(), 0, initialHeuristic));
        }
        catch (Exception e)
        {
            GD.PushError($"Error setting up initial planning state: {e.Message}");
            return null;
        }

        var visited = new HashSet<string>();
        int enqueuedCount = 0;
        int dequeuedCount = 0;

        while (openSet.Count > 0)
        {
            // Reduce log noise - only log occasionally
            if (dequeuedCount % 500 == 0)
            {
                GD.Print($"Planning: openSet={openSet.Count}, dequeued={dequeuedCount}");
            }

            // Get state with lowest f-score (g + h)
            openSet.Sort((a, b) => a.heuristic.CompareTo(b.heuristic));
            var (currentState, path, depth, _) = openSet[0];
            openSet.RemoveAt(0);
            dequeuedCount++;

            try
            {
                var stateHash = string.Join(",", currentState.Facts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
                if (visited.Contains(stateHash)) continue;
                visited.Add(stateHash);
            }
            catch (Exception e)
            {
                GD.PushError($"Error creating state hash: {e.Message}");
                continue;
            }

            try
            {
                if (currentState.Satisfies(goalState))
                {
                    GD.Print($"Found plan with {path.Count} steps (depth {depth})!");
                    var planStepNames = path.Select(s =>
                    {
                        try
                        {
                            var action = s.CreateAction(currentState);
                            return action?.GetType().Name ?? "UnknownAction";
                        }
                        catch (Exception e)
                        {
                            GD.PushError($"Error creating action for plan debug: {e.Message}");
                            return "ErrorAction";
                        }
                    });
                    GD.Print($"Plan: {string.Join(" -> ", planStepNames)}");
                    return new Plan(path, initialState);
                }
            }
            catch (Exception e)
            {
                GD.PushError($"Error checking state satisfaction: {e.Message}");
                continue;
            }

            // More aggressive depth limiting
            if (depth > 50)
            {
                GD.Print($"Skipping deep state (depth {depth})");
                continue;
            }

            // Regenerate steps if state has changed significantly (e.g., new sticks created)
            var currentSteps = allSteps;
            if (HasStateChangedSignificantly(currentState, initialState, dequeuedCount))
            {
                currentSteps = GenerateStepsForState(currentState, factories);
                // Only log regeneration occasionally to reduce noise
                if (dequeuedCount % 200 == 0)
                {
                    GD.Print($"Regenerated {currentSteps.Count} steps at depth {depth}");
                }
            }

            var applicableSteps = currentSteps.Where(s => s.CanRun(currentState)).ToList();
            // Initial state step analysis removed to reduce log noise

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

                    // Calculate heuristic for A* planning
                    try
                    {
                        float heuristic = CalculateHeuristic(newState, goalState) + depth + 1;
                        openSet.Add((newState, newPath, depth + 1, heuristic));
                    }
                    catch (Exception e)
                    {
                        GD.PushError($"Error calculating heuristic: {e.Message}");
                        continue;
                    }
                    enqueuedCount++;
                }
                catch (Exception e)
                {
                    GD.PushError($"Error applying step effects: {e.Message}");
                }

                // Limit open set size more aggressively
                if (openSet.Count > 1000)
                {
                    // Keep only the best candidates
                    openSet.Sort((a, b) => a.heuristic.CompareTo(b.heuristic));
                    openSet.RemoveRange(500, openSet.Count - 500);
                }
            }
        }

        GD.Print($"BFS ended: dequeued {dequeuedCount}, enqueued {enqueuedCount}, visited {visited.Count}. No plan found.");
        GD.Print($"Final state facts: {string.Join(", ", initialState.Facts.Select(kv => $"{kv.Key}={kv.Value}"))}");

        // Debug: Check what the best state we found looks like
        if (openSet.Count > 0)
        {
            openSet.Sort((a, b) => a.heuristic.CompareTo(b.heuristic));
            var bestState = openSet[0].state;
            GD.Print($"Best state found: {string.Join(", ", bestState.Facts.Select(kv => $"{kv.Key}={kv.Value}"))}");
            GD.Print($"Best state heuristic: {CalculateHeuristic(bestState, goalState)}");
            GD.Print($"Goal satisfaction: {bestState.Satisfies(goalState)}");
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
                heuristic += Math.Abs(currentCount - targetGoalCount2);
            }
            else if (!currentValue.Equals(goalFact.Value))
            {
                heuristic += 1f; // Boolean fact mismatch
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

    private static bool HasStateChangedSignificantly(State currentState, State referenceState, int dequeuedCount)
    {
        // Check if world has changed in ways that would create new steps
        var significantChanges = new[] {
            "WorldStickCount", "Available_Stick", "WorldTreeCount", "Available_Tree"
        };

        foreach (var fact in significantChanges)
        {
            var currentValue = currentState.Facts.GetValueOrDefault(fact);
            var referenceValue = referenceState.Facts.GetValueOrDefault(fact);

            if (!object.Equals(currentValue, referenceValue))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<Plan> PlanAsync(State initialState, State goalState)
    {
        return await Task.Run(() => Plan(initialState, goalState));
    }
}
