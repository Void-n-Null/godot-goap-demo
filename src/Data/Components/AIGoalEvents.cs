using System;
using Game.Data.UtilityAI;

namespace Game.Data.Components;

#nullable enable

/// <summary>
/// Centralizes goal-related event dispatch so the executor doesn't own delegate plumbing.
/// </summary>
public sealed class AIGoalEvents
{
    public event Action<IUtilityGoal?>? CannotPlan;
    public event Action<IUtilityGoal?>? PlanExecutionFailed;
    public event Action? PlanSucceeded;
    public event Action? GoalSatisfied;
    public event Action? NeedNewGoal;

    public void NotifyCannotPlan(IUtilityGoal? goal) => CannotPlan?.Invoke(goal);
    public void NotifyPlanExecutionFailed(IUtilityGoal? goal) => PlanExecutionFailed?.Invoke(goal);
    public void NotifyPlanSucceeded() => PlanSucceeded?.Invoke();
    public void NotifyGoalSatisfied() => GoalSatisfied?.Invoke();
    public void NotifyNeedNewGoal() => NeedNewGoal?.Invoke();
}

