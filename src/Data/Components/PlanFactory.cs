using System.Threading;
using System.Threading.Tasks;
using Game.Data.GOAP;
using Game.Data.UtilityAI;
using Game.Utils;

namespace Game.Data.Components;

#nullable enable
/// <summary>
/// Coordinates planning lifecycle: start planning jobs and process their completion results.
/// </summary>
public sealed class PlanCoordinator
{
    private int _nextToken;

    public PlanningJob BeginPlanning(State currentState, State goalState, IUtilityGoal goal)
    {
        int token = Interlocked.Increment(ref _nextToken);
        var task = AdvancedGoalPlanner.ForwardPlanAsync(currentState.Clone(), goalState);
        return new PlanningJob(token, goal, task);
    }

public PlanCompletionResult ProcessCompletion(
    PlanningJob job,
    IUtilityGoal? currentGoal,
    Entity entity,
    PlanRunner runner)
    {
        if (!job.Task.IsCompleted)
        {
            return PlanCompletionResult.StillRunning;
        }

        if (job.Task.IsFaulted)
        {
            LM.Error($"[{entity.Name}] Planning error for '{currentGoal?.Name}': {job.Task.Exception?.Message}");
            return PlanCompletionResult.Faulted;
        }

        var newPlan = job.Task.Result;

        if (job.Goal != currentGoal)
        {
            // Stale goal; discard without noise.
            return PlanCompletionResult.StaleGoal;
        }

        if (runner.HasActivePlan)
        {
            LM.Debug($"[{entity.Name}] Discarding completed plan - already have active plan");
            return PlanCompletionResult.ActivePlanExists;
        }

        if (newPlan != null && newPlan.Steps.Count > 0)
        {
            runner.SetPlan(newPlan);
            LM.Info($"[{entity.Name}] New plan for '{currentGoal?.Name}': {newPlan.Steps.Count} steps");
            return PlanCompletionResult.Installed;
        }

        LM.Warning($"[{entity.Name}] Cannot plan for '{currentGoal?.Name}' - no valid path to goal");
        return PlanCompletionResult.NoPath;
    }
}

public sealed class PlanningJob
{
    public int Token { get; }
    public IUtilityGoal Goal { get; }
    public Task<Plan> Task { get; }

    public PlanningJob(int token, IUtilityGoal goal, Task<Plan> task)
    {
        Token = token;
        Goal = goal;
        Task = task;
    }
}

public enum PlanCompletionResult
{
    StillRunning,
    Installed,
    NoPath,
    Faulted,
    StaleGoal,
    ActivePlanExists
}

