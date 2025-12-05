using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data.UtilityAI;
using Game.Utils;
using Game.Universe;

namespace Game.Data.Components;

/// <summary>
/// Evaluates multiple goals and assigns the highest-utility one to the executor.
/// Tracks failed goals to avoid repeatedly assigning impossible goals.
/// 
/// EVENT-DRIVEN: Only assigns new goals in response to executor events.
/// Never interrupts active plans with periodic re-evaluation.
/// </summary>
public class UtilityGoalSelector : IActiveComponent
{
    public Entity Entity { get; set; }

    private IUtilityGoal _currentGoal;

    // Available goals to choose from
    private List<IUtilityGoal> _availableGoals = [];

    // Track goals that failed to plan (couldn't find a path)
    private Dictionary<IUtilityGoal, float> _goalPlanFailureCooldowns = new();
    private const float DEFAULT_PLAN_FAILURE_COOLDOWN = 5.0f; // Fallback if goal doesn't override

    // Track goals that failed during execution
    private Dictionary<IUtilityGoal, float> _goalExecutionFailureCooldowns = new();
    private const float DEFAULT_EXECUTION_FAILURE_COOLDOWN = 2.0f; // Fallback if goal doesn't override

    private AIGoalExecutor _executor;

    public void Update(double delta)
    {
        // Selector is event-driven - only reacts to executor events
        // No periodic evaluation to avoid interrupting active plans
    }

    private void OnExecutorCannotPlan(IUtilityGoal goal)
    {
        string nameFirstWord = Entity.Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] ?? "Entity";
        string idFirst6 = Entity.Id.ToString().Length >= 6 ? Entity.Id.ToString().Substring(0, 6) : Entity.Id.ToString();
        float cooldown = GetPlanFailureCooldown(goal);
        LM.Warning($"[{nameFirstWord} {idFirst6}] Executor couldn't find plan for '{goal.Name}', applying {cooldown}s cooldown");
        _goalPlanFailureCooldowns[goal] = GameManager.Instance.CachedTimeMsec / 1000.0f + cooldown;
        _currentGoal = null;
        EvaluateAndSelectGoal();
    }

    private void OnExecutorPlanExecutionFailed(IUtilityGoal goal)
    {
        string nameFirstWord = Entity.Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] ?? "Entity";
        string idFirst6 = Entity.Id.ToString().Length >= 6 ? Entity.Id.ToString().Substring(0, 6) : Entity.Id.ToString();
        float cooldown = GetExecutionFailureCooldown(goal);
        LM.Warning($"[{nameFirstWord} {idFirst6}] Executor's plan execution failed for '{goal.Name}', applying {cooldown}s cooldown");

        // Always apply cooldown on execution failure to prevent jittery loops.
        // If it's truly urgent, they can try again after a short break.
        _goalExecutionFailureCooldowns[goal] = GameManager.Instance.CachedTimeMsec / 1000.0f + cooldown;

        _currentGoal = null;
        EvaluateAndSelectGoal();
    }

    private void OnExecutorPlanSucceeded()
    {
        // Plan succeeded, check if we should switch goals or continue
        LM.Debug($"[{Entity.Name}] OnExecutorPlanSucceeded - re-evaluating goals");
        EvaluateAndSelectGoal();
    }

    private void OnExecutorGoalSatisfied()
    {
        LM.Debug($"[{Entity.Name}] OnExecutorGoalSatisfied - clearing goal '{_currentGoal?.Name}'");
        // Clear cooldowns for the satisfied goal since it worked
        if (_currentGoal != null)
        {
            _goalPlanFailureCooldowns.Remove(_currentGoal);
            _goalExecutionFailureCooldowns.Remove(_currentGoal);
        }
        _currentGoal = null;
        EvaluateAndSelectGoal();
    }

    private void OnExecutorNeedNewGoal()
    {
        // Executor needs a goal, assign one immediately
        LM.Debug($"[{Entity.Name}] OnExecutorNeedNewGoal - executor has no goal");
        EvaluateAndSelectGoal();
    }

    public void ForceReevaluation()
    {
        // Clear current goal and immediately re-evaluate
        // This is used for high-priority interrupts (e.g. mate requests)
        _currentGoal = null;
        EvaluateAndSelectGoal();
    }

    private bool IsGoalOnCooldown(IUtilityGoal goal)
    {
        float currentTime = GameManager.Instance.CachedTimeMsec / 1000.0f;

        // Check plan failure cooldown
        if (_goalPlanFailureCooldowns.TryGetValue(goal, out float planCooldownEnd))
        {
            if (currentTime < planCooldownEnd)
            {
                return true;
            }
            // Cooldown expired, remove it
            _goalPlanFailureCooldowns.Remove(goal);
        }

        // Check execution failure cooldown
        if (_goalExecutionFailureCooldowns.TryGetValue(goal, out float execCooldownEnd))
        {
            if (currentTime < execCooldownEnd)
            {
                return true;
            }
            // Cooldown expired, remove it
            _goalExecutionFailureCooldowns.Remove(goal);
        }

        return false;
    }

    private void EvaluateAndSelectGoal()
    {
        // Only called in response to executor events - never interrupts active plans
        if (_availableGoals.Count == 0) return;
        if (_executor == null) return;

        IUtilityGoal bestGoal = null;
        float bestUtility = float.MinValue;

        LM.Debug($"[{Entity.Name}] EvaluateAndSelectGoal: evaluating {_availableGoals.Count} goals");
        
        for (int i = 0; i < _availableGoals.Count; i++)
        {
            var goal = _availableGoals[i];
            if (IsGoalOnCooldown(goal))
            {
                LM.Debug($"  - {goal.Name}: ON COOLDOWN");
                continue;
            }

            float utility = goal.CalculateUtility(Entity);
            LM.Debug($"  - {goal.Name}: utility={utility:F2}");
            if (utility > bestUtility)
            {
                bestUtility = utility;
                bestGoal = goal;
            }
        }

        if (bestGoal == null)
        {
            // Safety Net: If all goals are on cooldown, force IdleGoal
            var idleGoal = _availableGoals.FirstOrDefault(g => g is IdleGoal);
            if (idleGoal != null)
            {
                LM.Info($"[{Entity.Name}] All goals on cooldown, forcing IdleGoal fallback.");
                _goalPlanFailureCooldowns.Remove(idleGoal);
                _goalExecutionFailureCooldowns.Remove(idleGoal);
                bestGoal = idleGoal;
            }
            else
            {
                LM.Warning($"[{Entity.Name}] All goals are on cooldown and no IdleGoal found! Nothing to do!");
                _currentGoal = null;
                return;
            }
        }

        // Always switch to best available goal if current is null or different with significant utility
        if (_currentGoal == null || (_currentGoal != bestGoal && bestUtility > 0.05f))
        {
            var oldGoal = _currentGoal?.Name ?? "None";
            _currentGoal = bestGoal;

            // Bestow the new goal upon the executor
            _executor.SetGoal(_currentGoal);
        }
    }

    public void OnStart()
    {
        // Register available goals
        _availableGoals.Add(new EatFoodGoal());
        _availableGoals.Add(new StayWarmGoal());
        _availableGoals.Add(new SleepGoal());
        _availableGoals.Add(new IdleGoal());

        LM.Info($"[{Entity.Name}] UtilityAI started with {_availableGoals.Count} goals");
    }

    public void OnPostAttached()
    {
        // Hook up to executor events
        if (Entity.TryGetComponent(out _executor))
        {
            _executor.Events.CannotPlan += OnExecutorCannotPlan;
            _executor.Events.PlanExecutionFailed += OnExecutorPlanExecutionFailed;
            _executor.Events.PlanSucceeded += OnExecutorPlanSucceeded;
            _executor.Events.GoalSatisfied += OnExecutorGoalSatisfied;
            _executor.Events.NeedNewGoal += OnExecutorNeedNewGoal;

            // Evaluate and assign initial goal
            EvaluateAndSelectGoal();
        }
    }
    private static float GetPlanFailureCooldown(IUtilityGoal goal)
    {
        if (goal is IUtilityGoalCooldowns custom)
        {
            return Math.Max(0f, custom.PlanFailureCooldownSeconds);
        }
        return DEFAULT_PLAN_FAILURE_COOLDOWN;
    }

    private static float GetExecutionFailureCooldown(IUtilityGoal goal)
    {
        if (goal is IUtilityGoalCooldowns custom)
        {
            return Math.Max(0f, custom.ExecutionFailureCooldownSeconds);
        }
        return DEFAULT_EXECUTION_FAILURE_COOLDOWN;
    }
}

