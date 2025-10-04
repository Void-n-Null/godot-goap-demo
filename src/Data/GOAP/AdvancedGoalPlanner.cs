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
    /// Now delegates to State.GetDeterministicHash() which caches the result.
    /// </summary>
    private static int ComputeStateHash(State state)
    {
        return state?.GetDeterministicHash() ?? 0;
    }

    /// <summary>
    /// Creates a delta dictionary with only the step effects.
    /// Returns a lightweight delta instead of copying all facts.
    /// </summary>
    private static Dictionary<string, object> CreateStepDelta(State currentState, Step step)
    {
        // Only store the changes (delta), not the full state
        var delta = new Dictionary<string, object>(step.Effects.Count);

        foreach (var kvp in step.Effects)
        {
            delta[kvp.Key] = kvp.Value is Func<State, object> func
                ? func(currentState)
                : kvp.Value;
        }

        return delta;
    }

    /// <summary>
    /// Creates a successor state by applying a step to the current state.
    /// Uses layered state with parent pointer - only stores the delta to minimize allocations.
    /// </summary>
    private static (State state, PathNode path, double gCost, float fScore) CreateSuccessorState(
        State currentState,
        Step step,
        PathNode currentPath,
        double currentGCost,
        State goalState)
    {
        // Create layered state: parent + delta (avoids copying all facts)
        var delta = CreateStepDelta(currentState, step);
        var newState = new State(currentState, delta);
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

    private static List<Step> GenerateStepsForState(State state, State goalState)
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

        // Prune irrelevant steps to reduce search space
        return PruneWithDependencies(allSteps, goalState);
    }

    /// <summary>
    /// Prunes steps that are irrelevant to the goal using dependency analysis.
    /// Keeps steps that directly achieve the goal plus steps that enable them (transitive closure).
    /// </summary>
    private static List<Step> PruneWithDependencies(List<Step> allSteps, State goalState)
    {
        var relevant = new HashSet<Step>();
        var queue = new Queue<Step>();

        // Phase 1: Find steps that directly achieve goal
        foreach (var step in allSteps)
        {
            // Check if any effect contributes to any goal fact
            foreach (var goalFact in goalState.Facts)
            {
                if (step.Effects.ContainsKey(goalFact.Key))
                {
                    relevant.Add(step);
                    queue.Enqueue(step);
                    break;
                }
            }
        }

        // Phase 2: Backward chain - find steps that enable goal-achieving steps
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            // Find steps that satisfy this step's preconditions
            foreach (var step in allSteps)
            {
                if (relevant.Contains(step))
                    continue;

                // Does this step provide any precondition for current?
                bool providesPrerequisite = false;
                foreach (var precondition in current.Preconditions)
                {
                    if (step.Effects.ContainsKey(precondition.Key))
                    {
                        providesPrerequisite = true;
                        break;
                    }
                }

                if (providesPrerequisite)
                {
                    relevant.Add(step);
                    queue.Enqueue(step);
                }
            }
        }

        var pruned = relevant.ToList();

        // Debug output to show pruning effectiveness
        if (allSteps.Count > pruned.Count)
        {
            GD.Print($"[Pruning] Reduced steps from {allSteps.Count} to {pruned.Count} ({100.0 * pruned.Count / allSteps.Count:F1}% retained)");
        }

        return pruned;
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
        var steps = GenerateStepsForState(initialState, goalState);
        
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

            // Expand valid steps (avoid allocating intermediate list)
            foreach (var step in steps)
            {
                if (!step.CanRun(currentState))
                    continue;

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

        var steps = GenerateStepsForState(initialState, goalState);

        // Thread-safe collections for parallel processing
        var openSet = new PriorityQueue<(State state, PathNode path, double gCost, float fScore), float>();
        var openSetLock = new object();
        // Pre-size visited set to reduce resizing during search (typical search explores 100-1000 states)
        var visited = new ConcurrentDictionary<int, bool>(System.Environment.ProcessorCount, 512);
        
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

                    // Collect valid steps for this state
                    // We need to materialize here to decide whether to parallelize
                    var validSteps = new List<Step>(steps.Count);
                    foreach (var s in steps)
                    {
                        if (s.CanRun(currentState))
                            validSteps.Add(s);
                    }

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
