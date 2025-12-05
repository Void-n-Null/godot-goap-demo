using System;
using Game.Data;
using Game.Data.GOAP;
using Game.Data.UtilityAI;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Lightweight helper that owns plan execution and encapsulates ticking logic.
/// It keeps AIGoalExecutor focused on orchestration rather than step-by-step running.
/// </summary>
public sealed class PlanRunner
{
    public Plan CurrentPlan { get; private set; }

    public bool HasActivePlan => CurrentPlan != null && !CurrentPlan.IsComplete;

    public void SetPlan(Plan plan)
    {
        CurrentPlan = plan;
    }

    public void ClearPlan()
    {
        CurrentPlan = null;
    }

    public void CancelPlan(Entity entity)
    {
        if (HasActivePlan)
        {
            CurrentPlan.Cancel(entity);
        }
        CurrentPlan = null;
    }

    public PlanRunStatus Tick(Entity entity, IUtilityGoal goal, double deltaSeconds)
    {
        if (!HasActivePlan)
        {
            return PlanRunStatus.NoPlan;
        }

        var goalChecker = BuildGoalChecker(goal);
        var result = CurrentPlan.Tick(entity, (float)deltaSeconds, goalChecker);

        switch (result)
        {
            case PlanTickResult.Running:
                return PlanRunStatus.Running;
            case PlanTickResult.Succeeded:
                CurrentPlan = null;
                return PlanRunStatus.Succeeded;
            case PlanTickResult.Failed:
                CurrentPlan = null;
                return PlanRunStatus.Failed;
            default:
                return PlanRunStatus.NoPlan;
        }
    }

    private static Func<Entity, bool> BuildGoalChecker(IUtilityGoal goal)
    {
        if (goal == null) return null;

        return (agent) =>
        {
            try
            {
                return goal.IsSatisfied(agent);
            }
            catch (Exception ex)
            {
                LM.Error($"Goal satisfaction check failed: {ex.Message}");
                return false;
            }
        };
    }
}

public enum PlanRunStatus
{
    NoPlan,
    Running,
    Succeeded,
    Failed
}

