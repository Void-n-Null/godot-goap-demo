using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System;
using Godot;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Game.Utils;

namespace Game.Data.GOAP;


public class PlanningConfig
{
    public required int MaxDepth { get; set; }
    public required int MaxOpenSetSize { get; set; }
}

public static class AdvancedGoalPlanner
{
    private static readonly PlanningConfig DefaultConfig = new() { MaxDepth = 50, MaxOpenSetSize = 1000 };

    private static readonly Lazy<List<IStepFactory>> _cachedFactories = new(
        () => LocateStepFactoriesInternal(), 
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static List<IStepFactory> CachedFactories => _cachedFactories.Value;

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
    /// Creates a successor state by applying a step to the current state.
    /// Uses layered state with parent pointer - only stores the delta to minimize allocations.
    /// </summary>
    private static (State state, PathNode path, double gCost, float fScore) CreateSuccessorState(
        State currentState,
        Step step,
        PathNode currentPath,
        double currentGCost,
        State goalState,
        State implicitGoals)
    {
        var newState = currentState.Clone();

        var compiledEffects = step.CompiledEffects;
        foreach (var kvp in compiledEffects)
        {
            var resolvedValue = ResolveEffectValue(kvp.Value, currentState);
            newState.Set(kvp.Key, resolvedValue);
        }

        var newPath = new PathNode(step, currentPath); // O(1) instead of O(n)

        var stepCost = step.GetCost(currentState);
        var newGCost = currentGCost + stepCost;
        var heuristic = StateComparison.CalculateStateComparisonHeuristic(newState, goalState, implicitGoals);
        
        // Add significant depth penalty to strongly prefer shorter paths
        // Without this, A* explores deep cooking paths before finding the simpler direct path
        // Each step adds 0.5 to fScore, making 50-step paths significantly more expensive than 3-step paths
        var depthPenalty = newPath.Depth * 1f;
        var fScore = (float)newGCost + heuristic + depthPenalty;

        return (newState, newPath, newGCost, fScore);
    }

    private static FactValue ResolveEffectValue(object rawValue, State referenceState)
    {
        return rawValue switch
        {
            FactValue fv => fv,
            Func<State, FactValue> factFunc => factFunc(referenceState),
            Func<State, object> objFunc => ConvertToFactValue(objFunc(referenceState)),
            _ => ConvertToFactValue(rawValue)
        };
    }

    private static FactValue ConvertToFactValue(object value)
    {
        return value switch
        {
            null => default,
            FactValue fv => fv,
            bool b => b,
            int i => i,
            float f => f,
            double d => (float)d,
            _ => throw new ArgumentException($"Unsupported fact value type {value?.GetType().Name}")
        };
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
                LM.Error($"Error in step factory {factory.GetType().Name}: {e.Message}");
                LM.Error($"Stack trace: {e.StackTrace}");
            }
        }

        // Prune irrelevant steps to reduce search space
        return PruneWithDependencies(allSteps, goalState);
    }

    /// <summary>
    /// Infers implicit resource requirements for goal facts by inspecting producer steps.
    /// </summary>
    private static State DeriveImplicitRequirements(State currentState, State goalState, List<Step> availableSteps)
    {
        var implicitState = new State();

        foreach (var goalFact in goalState.FactsById)
        {
            // Skip prerequisites that are already satisfied
            if (currentState.TryGet(goalFact.Key, out var curVal) && curVal.Equals(goalFact.Value))
                continue;

            var producer = availableSteps.FirstOrDefault(step =>
            {
                var effects = step.CompiledEffects;
                for (int i = 0; i < effects.Length; i++)
                {
                    if (effects[i].Key == goalFact.Key)
                    {
                        var effectValue = FactValue.From(effects[i].Value);
                        if (effectValue.Equals(goalFact.Value)) return true;
                    }
                }
                return false;
            });

            if (producer == null)
                continue;

            foreach (var pre in producer.Preconditions.FactsById)
            {
                if (pre.Value.Type == FactType.Int || pre.Value.Type == FactType.Float)
                {
                    implicitState.Set(pre.Key, pre.Value);
                }
            }
        }

        return implicitState;
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
            var compiledEffects = step.CompiledEffects;
            foreach (var goalFact in goalState.FactsById)
            {
                bool found = false;
                for (int i = 0; i < compiledEffects.Length; i++)
                {
                    if (compiledEffects[i].Key == goalFact.Key)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
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
                var stepEffects = step.CompiledEffects;
                foreach (var precondition in current.Preconditions.FactsById)
                {
                    for (int i = 0; i < stepEffects.Length; i++)
                    {
                        if (stepEffects[i].Key == precondition.Key)
                        {
                            providesPrerequisite = true;
                            break;
                        }
                    }
                    if (providesPrerequisite) break;
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
            LM.Debug($"[Pruning] Reduced steps from {allSteps.Count} to {pruned.Count}");
            LM.Debug($"[Pruning] Kept steps: {string.Join(", ", pruned.Select(s => s.Name))}");
        }

        return pruned;
    }

    /// <summary>
    /// Locate all step factories in the current assembly
    /// </summary>
    /// <returns>List of step factories</returns>
    private static List<IStepFactory> LocateStepFactoriesInternal(Assembly assembly = null)
    {
        if (assembly == null)
            assembly = Assembly.GetExecutingAssembly();

        // Discover factories reflectively (thread-safe via Lazy<T>)
        return
        [
            .. assembly.GetTypes()
            .Where(t => typeof(IStepFactory).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Select(t => (IStepFactory)Activator.CreateInstance(t)!)
        ];
    }

    //I know this implies a backward planning option... But for now, we'll stick to forward planning.
    //I want to eventually implement a backward planning option though... I don't think our state and step 
    //data is set up for backward planning...
    public static Plan ForwardPlan(State initialState, State goalState, PlanningConfig config = null)
    {
        config ??= DefaultConfig;
        //Alright, when we start, these are all the steps we can take.
        var steps = GenerateStepsForState(initialState, goalState);
        var implicitGoals = DeriveImplicitRequirements(initialState, goalState, steps);
        
        // Debug: Log goal state and available steps (using GD.Print to bypass LM toggle)
        LM.Debug($"[Planner] Goal: {string.Join(", ", goalState.FactsById.Select(f => $"{FactRegistry.GetName(f.Key)}={f.Value}"))}");
        LM.Debug($"[Planner] Available steps ({steps.Count}): {string.Join(", ", steps.Select(s => s.Name))}");
        LM.Debug($"[Planner] Initial state: {initialState}");

        //Now then... Let's begin our A* Forward planning!
        var openSet = new PriorityQueue<(State state, PathNode path, double gCost, float fScore), float>();
        //We only needed defensive and silent fails before because we did not have a good heuristic function before.
        //We shouldn't have been suppressing initialisation errors before...
        var initialHeuristic = StateComparison.CalculateStateComparisonHeuristic(initialState, goalState, implicitGoals);
        //The first state we have is a duplicate of the initial state for us to manipulate as needed.
        openSet.Enqueue((initialState.Clone(), null, 0.0, initialHeuristic), initialHeuristic);

        var visited = new HashSet<int>();



        while (openSet.Count > 0)
        {
            // Get state with lowest f-score (g + h)
            var (currentState, pathNode, gCost, _) = openSet.Dequeue();


            // Check if already visited
            var stateHash = currentState?.GetDeterministicHash() ?? 0;
            if (!visited.Add(stateHash)) continue;

            // Check if goal reached
            if (currentState.Satisfies(goalState))
            {
                var path = pathNode?.ReconstructPath() ?? new List<Step>();
                LM.Info($"Plan found: {path.Count} steps, total cost {gCost:F1}");
                
                // Log each step in the plan for debugging
                if (path.Count > 0)
                {
                    var stepNames = string.Join(" → ", path.Select(s => s.Name));
                    LM.Info($"Plan steps: {stepNames}");
                }
                
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

                var successor = CreateSuccessorState(currentState, step, pathNode, gCost, goalState, implicitGoals);
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
        var implicitGoals = DeriveImplicitRequirements(initialState, goalState, steps);

        // Thread-safe collections for parallel processing
        var openSet = new PriorityQueue<(State state, PathNode path, double gCost, float fScore), float>();
        var openSetLock = new object();
        // Pre-size visited set to reduce resizing during search (typical search explores 100-1000 states)
        var visited = new ConcurrentDictionary<int, bool>(System.Environment.ProcessorCount, 512);

        var initialHeuristic = StateComparison.CalculateStateComparisonHeuristic(initialState, goalState, implicitGoals);
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
                    var stateHash = currentState?.GetDeterministicHash() ?? 0;
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
                                LM.Info($"Plan found (parallel): {path.Count} steps, total cost {gCost:F1}");
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
                            var successor = CreateSuccessorState(currentState, step, pathNode, gCost, goalState, implicitGoals);
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
                            var successor = CreateSuccessorState(currentState, step, pathNode, gCost, goalState, implicitGoals);
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
