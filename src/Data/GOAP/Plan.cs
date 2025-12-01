using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.GOAP;
using Godot;
using System.Linq;
using Game.Utils;

namespace Game.Data.GOAP;

public enum PlanTickResult
{
	/// <summary>Plan is still executing</summary>
	Running,
	/// <summary>Plan completed successfully</summary>
	Succeeded,
	/// <summary>Plan failed during execution</summary>
	Failed
}

public sealed class Plan
{
	private readonly List<Step> _steps;
	public IReadOnlyList<Step> Steps => _steps.AsReadOnly();
	private int _currentStepIndex = -1;
	private IAction _currentAction;
	private State _currentState; 

	public State CurrentState => _currentState;
	public bool IsComplete { get; private set; }
	public bool Succeeded { get; private set; }
	
	/// <summary>
	/// Gets the current result status of the plan
	/// </summary>
	public PlanTickResult Result
	{
		get
		{
			if (!IsComplete) return PlanTickResult.Running;
			return Succeeded ? PlanTickResult.Succeeded : PlanTickResult.Failed;
		}
	}

	public Plan(IEnumerable<Step> steps, State initialState)
	{
		_steps = new List<Step>(steps ?? Array.Empty<Step>());
		_currentState = initialState?.Clone() ?? State.Empty();
		IsComplete = _steps.Count == 0;
		Succeeded = IsComplete;
	}

public PlanTickResult Tick(Entity agent, float dt, Func<Entity, bool> goalSatisfied = null)
	{
			if (IsComplete) return Result;

	if (goalSatisfied != null && goalSatisfied(agent))
	{
		LM.Info("Plan goal satisfied during tick, ending plan early.");
		IsComplete = true;
		Succeeded = true;
		if (_currentAction != null)
		{
			_currentAction.Exit(agent, ActionExitReason.Completed);
			_currentAction = null;
		}
		return PlanTickResult.Succeeded;
	}

		if (_currentAction is IRuntimeGuard guard && !guard.StillValid(agent))
		{
			LM.Warning($"Plan step {_currentStepIndex} no longer valid, failing plan");
			_currentAction.Exit(agent, ActionExitReason.Failed);
			_currentAction = null;
			IsComplete = true;
			Succeeded = false;
			return PlanTickResult.Failed;
		}

		if (_currentAction == null || _currentStepIndex < 0)
		{
			_currentStepIndex++;
			if (_currentStepIndex >= _steps.Count)
			{
				IsComplete = true;
				Succeeded = true;
				return PlanTickResult.Succeeded;
			}

			var step = _steps[_currentStepIndex];
			LM.Debug($"Plan starting step {_currentStepIndex}: {step.Name}");

			try
			{
				_currentAction = step.CreateAction();
				LM.Debug($"  Action created: {_currentAction?.Name ?? "null"}");
				_currentAction.Enter(agent);
				LM.Debug($"  Enter completed");
			}
			catch (Exception ex)
			{
				LM.Error($"  Exception during step creation/enter: {ex.Message}");
				throw;
			}
		}

		var status = _currentAction.Update(agent, dt);
		
		if (status == ActionStatus.Running) return PlanTickResult.Running;

		var reason = status == ActionStatus.Succeeded ? ActionExitReason.Completed : ActionExitReason.Failed;
		_currentAction.Exit(agent, reason);
		if (status == ActionStatus.Failed) _currentAction.Fail("Plan preempted step due to failure");

		if (status == ActionStatus.Succeeded)
		{
			var step = _steps[_currentStepIndex];
			LM.Debug($"Step {_currentStepIndex} succeeded, applying {step.Effects.Count} effects");
			
			foreach(var (key, val) in step.Effects)
			{
				FactValue finalValue;
				if (val is Func<State, FactValue> func)
				{
					finalValue = func(_currentState);
				}
				else if (val is FactValue fv)
				{
					finalValue = fv;
				}
				else
				{
					try 
					{ 
						// Fallback conversion using static method (no dynamic overhead)
						finalValue = FactValue.From(val); 
					} 
					catch 
					{ 
						// Skip invalid effect values
						continue; 
					}
				}
				_currentState.Set(key, finalValue);
			}
		}
		else
		{
			LM.Error($"Plan step {_currentStepIndex} failed - check action Fail logs for reason");
			IsComplete = true;
			Succeeded = false;
			return PlanTickResult.Failed;
		}

		_currentAction = null;

	// Start next step on next tick, but if goal already satisfied, finish now
	if (goalSatisfied != null && goalSatisfied(agent))
	{
		LM.Info("Plan goal satisfied after step completion, ending plan.");
		IsComplete = true;
		Succeeded = true;
		return PlanTickResult.Succeeded;
	}

	if (_currentStepIndex >= _steps.Count - 1)
	{
		// Final step completed - ensure we clean up the action before marking complete
		if (_currentAction != null)
		{
			_currentAction.Exit(agent, ActionExitReason.Completed);
			_currentAction = null;
		}
		IsComplete = true;
		Succeeded = true;
		return PlanTickResult.Succeeded;
	}

		return PlanTickResult.Running;
	}

	public void Cancel(Entity agent)
	{
		if (_currentAction != null)
		{
			_currentAction.Exit(agent, ActionExitReason.Cancelled);
			_currentAction = null;
		}
		_currentStepIndex = -1;
		IsComplete = true;
		Succeeded = false;
	}

	public bool SatisfiesGoal(State goal)
	{
		return CurrentState.Satisfies(goal);
	}
}
