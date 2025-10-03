using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Components;
using Game.Data.UtilityAI;

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
	private const float PLAN_FAILURE_COOLDOWN = 5.0f; // Don't retry for 5 seconds if no plan found

	// Track goals that failed during execution
	private Dictionary<IUtilityGoal, float> _goalExecutionFailureCooldowns = new();
	private const float EXECUTION_FAILURE_COOLDOWN = 2.0f; // Small cooldown for execution failures

	private AIGoalExecutor _executor;

	public void Update(double delta)
	{
		// Selector is event-driven - only reacts to executor events
		// No periodic evaluation to avoid interrupting active plans
	}

	private void OnExecutorCannotPlan(IUtilityGoal goal)
	{
		GD.Print($"[{Entity.Name}] Executor couldn't find plan for '{goal.Name}', applying {PLAN_FAILURE_COOLDOWN}s cooldown");
		_goalPlanFailureCooldowns[goal] = Time.GetTicksMsec() / 1000.0f + PLAN_FAILURE_COOLDOWN;
		_currentGoal = null;
		EvaluateAndSelectGoal();
	}

	private void OnExecutorPlanExecutionFailed(IUtilityGoal goal)
	{
		GD.Print($"[{Entity.Name}] Executor's plan execution failed for '{goal.Name}', applying {EXECUTION_FAILURE_COOLDOWN}s cooldown");
		_goalExecutionFailureCooldowns[goal] = Time.GetTicksMsec() / 1000.0f + EXECUTION_FAILURE_COOLDOWN;
		_currentGoal = null;
		EvaluateAndSelectGoal();
	}

	private void OnExecutorPlanSucceeded()
	{
		// Plan succeeded, goal should be satisfied soon
		// We can wait for the periodic evaluation
	}

	private void OnExecutorGoalSatisfied()
	{

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
		EvaluateAndSelectGoal();
	}

	private bool IsGoalOnCooldown(IUtilityGoal goal)
	{
		float currentTime = Time.GetTicksMsec() / 1000.0f;

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

		// Calculate utility for each goal, excluding those on cooldown
		var goalUtilities = _availableGoals
			.Where(g => !IsGoalOnCooldown(g))
			.Select(g => new { Goal = g, Utility = g.CalculateUtility(Entity) })
			.OrderByDescending(x => x.Utility)
			.ToList();

		if (goalUtilities.Count == 0)
		{
			GD.Print($"[{Entity.Name}] All goals are on cooldown, nothing to do!");
			_currentGoal = null;
			return;
		}

		var bestGoal = goalUtilities.First();

		// Always switch to best available goal if current is null or different with significant utility
		if (_currentGoal == null || (_currentGoal != bestGoal.Goal && bestGoal.Utility > 0.05f))
		{
			var oldGoal = _currentGoal?.Name ?? "None";
			_currentGoal = bestGoal.Goal;
			
			// Bestow the new goal upon the executor
			_executor.SetGoal(_currentGoal);
		}
	}

	public void OnStart()
	{
		// Register available goals
		_availableGoals.Add(new EatFoodGoal());
		_availableGoals.Add(new StayWarmGoal());
		_availableGoals.Add(new IdleGoal());

		GD.Print($"[{Entity.Name}] UtilityAI started with {_availableGoals.Count} goals");
	}

	public void OnPostAttached()
	{
		// Hook up to executor events
		if (Entity.TryGetComponent(out _executor))
		{
			_executor.OnCannotPlan += OnExecutorCannotPlan;
			_executor.OnPlanExecutionFailed += OnExecutorPlanExecutionFailed;
			_executor.OnPlanSucceeded += OnExecutorPlanSucceeded;
			_executor.OnGoalSatisfied += OnExecutorGoalSatisfied;
			_executor.OnNeedNewGoal += OnExecutorNeedNewGoal;

			// Evaluate and assign initial goal
			EvaluateAndSelectGoal();
		}
	}
}

