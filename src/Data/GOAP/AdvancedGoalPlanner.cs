using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System;
using Godot;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Data.GOAP;


public class PlanningConfig {
    public required int MaxDepth { get; set; }
    public required int MaxOpenSetSize { get; set; }
}

public static class AdvancedGoalPlanner
{
    private static readonly PlanningConfig DefaultConfig = new() { MaxDepth = 50, MaxOpenSetSize = 1000 };

    private static List<IStepFactory> _cachedFactories;
    private static List<IStepFactory> CachedFactories => LocateStepFactories();

    #region Helper Methods

    /// <summary>
    /// Node for parent-pointer path tracking. Eliminates O(n²) list copying.
    /// Path is reconstructed only when goal is found.
    /// </summary>
    private class PathNode(Step step, PathNode parent)
    {
        public Step Step { get; } = step;
        public PathNode Parent { get; } = parent;
        public int Depth { get; } = parent == null ? 0 : parent.Depth + 1;

        /// <summary>
        /// Reconstructs the full path by traversing parent pointers
        /// </summary>
        public List<Step> ReconstructPath()
        {
            var steps = new List<Step>(Depth);
            var current = this;
            while (current != null && current.Step != null)
            {
                steps.Add(current.Step);
                current = current.Parent;
            }
            steps.Reverse();
            return steps;
        }
    }

    /// <summary>
    /// Computes a deterministic int hash for a state to track visited states.
    /// Uses a stable key ordering and System.HashCode to avoid allocations from
    /// string.Join / LINQ each lookup.
    /// </summary>
    private static int ComputeStateHash(State state)
    {
        if (state?.Facts == null || state.Facts.Count == 0) return 0;

        // Get keys and sort deterministically (ordinal)
        var keys = state.Facts.Keys.ToArray();
        Array.Sort(keys, StringComparer.Ordinal);

        int hash = 0;
        foreach (var key in keys)
        {
            var value = state.Facts[key];
            hash = HashCode.Combine(hash, key, value);
        }

        return hash;
    }

    /// <summary>
    /// Applies step effects to current state to create new state facts.
    /// Uses a pre-sized dictionary to reduce allocations.
    /// </summary>
    private static Dictionary<string, object> ApplyStepEffects(State currentState, Step step)
    {
        // Pre-size dictionary to avoid resizing during adds
        var newFacts = new Dictionary<string, object>(currentState.Facts.Count + step.Effects.Count);
        
        // Copy current facts
        foreach (var kvp in currentState.Facts)
        {
            newFacts[kvp.Key] = kvp.Value;
        }
        
        // Apply step effects (may overwrite existing facts)
        foreach (var kvp in step.Effects)
        {
            object newValue = kvp.Value is Func<State, object> func 
                ? func(currentState) 
                : kvp.Value;
            newFacts[kvp.Key] = newValue;
        }
        
        return newFacts;
    }

    /// <summary>
    /// Creates a successor state by applying a step to the current state.
    /// Uses parent-pointer pattern to eliminate O(n) path copying.
    /// </summary>
    private static (State state, PathNode path, double gCost, float fScore) CreateSuccessorState(
        State currentState, 
        Step step, 
        PathNode currentPath, 
        double currentGCost, 
        State goalState)
    {
        var newFacts = ApplyStepEffects(currentState, step);
        var newState = new State(newFacts);
        var newPath = new PathNode(step, currentPath); // O(1) instead of O(n)
        
        double stepCost = step.GetCost(currentState);
        double newGCost = currentGCost + stepCost;
        float heuristic = StateComparison.CalculateStateComparisonHeuristic(newState, goalState);
        float fScore = (float)newGCost + heuristic;
        
        return (newState, newPath, newGCost, fScore);
    }

    /// <summary>
    /// Prunes a priority queue open set by keeping only the best candidates
    /// (lowest fScore). Since PriorityQueue does not support removing worst
    /// items directly, we dequeue the best half and rebuild the queue.
    /// </summary>
    private static void PruneOpenSetPriority(
        PriorityQueue<(State state, PathNode path, double gCost, float fScore), float> openSet,
        int maxSize)
    {
        if (openSet.Count > maxSize)
        {
            int keepCount = Math.Max(1, maxSize / 2);

            var kept = new List<((State state, PathNode path, double gCost, float fScore) element, float priority)>(keepCount);
            for (int i = 0; i < keepCount && openSet.TryDequeue(out var element, out var priority); i++)
            {
                kept.Add((element, priority));
            }

            openSet.Clear();
            foreach (var item in kept)
            {
                openSet.Enqueue(item.element, item.priority);
            }
        }
    }

    #endregion

    private static List<Step> GenerateStepsForState(State state)
    {
        var allSteps = new List<Step>();
        foreach (var factory in CachedFactories)
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

    /// <summary>
    /// Locate all step factories in the current assembly
    /// </summary>
    /// <returns>List of step factories</returns>
    private static List<IStepFactory> LocateStepFactories(Assembly assembly = null)
    {
        if (assembly == null)
            assembly = Assembly.GetExecutingAssembly();

        // Discover factories reflectively only once
        return _cachedFactories ??= 
        [
            .. assembly.GetTypes()
            .Where(t => typeof(IStepFactory).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Select(t => (IStepFactory)Activator.CreateInstance(t)!)
        ];
    }

    //I know this implies a backward planning option... But for now, we'll stick to forward planning.
    //I want to eventually implement a backward planning option though... I don't think our state and step 
    //data is set up for backward planning...
    public static Plan ForwardPlan(State initialState, State goalState, PlanningConfig config = null){
        config ??= DefaultConfig;
        //Alright, when we start, these are all the steps we can take.
        var steps = GenerateStepsForState(initialState);
        
        //Now then... Let's begin our A* Forward planning!
        var openSet = new PriorityQueue<(State state, PathNode path, double gCost, float fScore), float>();
        //We only needed defensive and silent fails before because we did not have a good heuristic function before.
        //We shouldn't have been suppressing initialisation errors before...
        var initialHeuristic = StateComparison.CalculateStateComparisonHeuristic(initialState, goalState);
        //The first state we have is a duplicate of the initial state for us to manipulate as needed.
        openSet.Enqueue((initialState.Clone(), null, 0.0, initialHeuristic), initialHeuristic);

        var visited = new HashSet<int>();

        

        while (openSet.Count > 0)
        {
            // Get state with lowest f-score (g + h)
            var (currentState, pathNode, gCost, _) = openSet.Dequeue();


            // Check if already visited
            var stateHash = ComputeStateHash(currentState);
            if (!visited.Add(stateHash)) continue;

            // Check if goal reached
            if (currentState.Satisfies(goalState))
            {
                var path = pathNode?.ReconstructPath() ?? new List<Step>();
                GD.Print($"Plan found: {path.Count} steps, total cost {gCost:F1}");
                return new Plan(path, initialState);
            }

            // Depth limiting
            int depth = pathNode?.Depth ?? 0;
            if (depth > config.MaxDepth)
            {
                continue;
            }

            // Get valid steps for current state
            var validSteps = steps.Where(s => s.CanRun(currentState)).ToList();

            // Expand each valid step
            foreach (var step in validSteps)
            {
                var successor = CreateSuccessorState(currentState, step, pathNode, gCost, goalState);
                openSet.Enqueue(successor, successor.fScore);

                // Prune open set if needed
                PruneOpenSetPriority(openSet, config.MaxOpenSetSize);
            }
        }
        return null;
    }

    /// <summary>
    /// Parallel forward planner using beam search approach
    /// Expands multiple promising states concurrently for better performance
    /// </summary>
    public static Plan ForwardPlanParallel(State initialState, State goalState, PlanningConfig config = null)
    {
        config ??= DefaultConfig;

        var steps = GenerateStepsForState(initialState);
        
        // Thread-safe collections for parallel processing
        var openSet = new PriorityQueue<(State state, PathNode path, double gCost, float fScore), float>();
        var openSetLock = new object();
        var visited = new ConcurrentDictionary<int, bool>();
        
        var initialHeuristic = StateComparison.CalculateStateComparisonHeuristic(initialState, goalState);
        openSet.Enqueue((initialState.Clone(), null, 0.0, initialHeuristic), initialHeuristic);

        // Beam width - number of states to expand in parallel
        const int beamWidth = 4;
        
        // Track solution (thread-safe)
        // Use CancellationToken to avoid lock contention on every check
        Plan foundPlan = null;
        var planLock = new object();
        var cts = new CancellationTokenSource();

        while (true)
        {
            // Select top candidates for parallel expansion without draining entire structure
            List<(State state, PathNode path, double gCost, float fScore)> candidates = new();
            lock (openSetLock)
            {
                for (int i = 0; i < beamWidth && openSet.Count > 0; i++)
                {
                    candidates.Add(openSet.Dequeue());
                }
            }

            if (candidates.Count == 0)
                break;

            // Expand candidates in parallel
            var parallelOptions = new ParallelOptions { CancellationToken = cts.Token };
            
            try
            {
                Parallel.ForEach(candidates, parallelOptions, (candidate, loopState) =>
                {
                    // Quick check without lock - if token is cancelled, a solution was found
                    if (cts.Token.IsCancellationRequested)
                    {
                        loopState.Stop();
                        return;
                    }

                    var (currentState, pathNode, gCost, _) = candidate;

                    // Check if already visited
                    var stateHash = ComputeStateHash(currentState);
                    if (!visited.TryAdd(stateHash, true))
                        return;

                    // Check if goal is reached
                    if (currentState.Satisfies(goalState))
                    {
                        var path = pathNode?.ReconstructPath() ?? new List<Step>();
                        lock (planLock)
                        {
                            // Only set if no solution found yet, or this one is better
                            if (foundPlan == null || path.Count < foundPlan.Steps.Count)
                            {
                                foundPlan = new Plan(path, initialState);
                                GD.Print($"Plan found (parallel): {path.Count} steps, total cost {gCost:F1}");
                            }
                        }
                        cts.Cancel(); // Signal all threads to stop
                        loopState.Stop();
                        return;
                    }

                    // Depth limiting
                    int depth = pathNode?.Depth ?? 0;
                    if (depth > config.MaxDepth)
                        return;

                    // Get valid steps for this state
                    var validSteps = steps.Where(s => s.CanRun(currentState)).ToList();

                    // Expand valid steps (parallelize if many steps)
                    if (validSteps.Count > 8)
                    {
                        // Parallel step expansion for many steps
                        var stepResults = new ConcurrentBag<(State state, PathNode path, double gCost, float fScore)>();
                        
                        Parallel.ForEach(validSteps, parallelOptions, step =>
                        {
                            var successor = CreateSuccessorState(currentState, step, pathNode, gCost, goalState);
                            stepResults.Add(successor);
                        });

                        foreach (var successor in stepResults)
                        {
                            lock (openSetLock)
                            {
                                openSet.Enqueue(successor, successor.fScore);
                            }
                        }
                    }
                    else
                    {
                        // Sequential for small number of steps
                        foreach (var step in validSteps)
                        {
                            var successor = CreateSuccessorState(currentState, step, pathNode, gCost, goalState);
                            lock (openSetLock)
                            {
                                openSet.Enqueue(successor, successor.fScore);
                            }
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when solution is found - just exit the parallel loop
            }

            // Check if solution was found
            lock (planLock)
            {
                if (foundPlan != null)
                {
                    cts.Dispose();
                    return foundPlan;
                }
            }

            // Prune open set if needed
            lock (openSetLock)
            {
                PruneOpenSetPriority(openSet, config.MaxOpenSetSize);
            }
        }

        cts.Dispose();
        return null;
    }

    public static async Task<Plan> ForwardPlanAsync(State initialState, State goalState, PlanningConfig config = null)
    {
        return await Task.Run(() => ForwardPlan(initialState, goalState, config));
    }

    public static async Task<Plan> ForwardPlanParallelAsync(State initialState, State goalState, PlanningConfig config = null)
    {
        return await Task.Run(() => ForwardPlanParallel(initialState, goalState, config));
    }
}
