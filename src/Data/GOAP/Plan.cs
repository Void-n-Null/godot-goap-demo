using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.GOAP;
using Godot;
using System.Linq;

namespace Game.Data.GOAP;

public sealed class Plan
{
	private readonly List<Step> _steps;
	public IReadOnlyList<Step> Steps => _steps.AsReadOnly();
	private int _currentStepIndex = -1;
	private IAction _currentAction;
	private Dictionary<string, object> _worldFacts = new(); // Mutable facts for execution tracking

	public State CurrentState { get; private set; }
	public bool IsComplete { get; private set; }
	public bool Succeeded { get; private set; }

	public Plan(IEnumerable<Step> steps, State initialState)
	{
		_steps = new List<Step>(steps ?? Array.Empty<Step>());
		CurrentState = initialState?.Clone() ?? State.Empty();
		_worldFacts = new Dictionary<string, object>(CurrentState.Facts); // Initial mutable facts
		IsComplete = _steps.Count == 0;
		Succeeded = IsComplete; // Empty plan is trivially succeeded
	}

	public bool Tick(Entity agent, float dt)
	{
		if (IsComplete) return true;

		// Check runtime guard validity for current action
		if (_currentAction is IRuntimeGuard guard && !guard.StillValid(agent))
		{
			GD.Print($"Plan step {_currentStepIndex} no longer valid, failing plan");
			_currentAction.Exit(agent, ActionExitReason.Failed);
			_currentAction = null;
			IsComplete = true;
			Succeeded = false;
			return true;
		}

		// Advance to next step if current action done
		if (_currentAction == null || _currentStepIndex < 0)
		{
			_currentStepIndex++;
			if (_currentStepIndex >= _steps.Count)
			{
				IsComplete = true;
				Succeeded = true;
				return true;
			}

			var step = _steps[_currentStepIndex];
			GD.Print($"Plan starting step {_currentStepIndex}: {step.Preconditions.Count} preconditions, {step.Effects.Count} effects");

			_currentAction = step.CreateAction();
			_currentAction.Enter(agent);
			GD.Print($"{_currentStepIndex + 1}. {_currentAction.GetType().Name}");
		}

		var status = _currentAction.Update(agent, dt);  // Pass real dt to action
		if (status != ActionStatus.Running){
			GD.Print($"Step {_currentStepIndex} {_currentAction.GetType().Name} Update returned {status}");
		}
		

		if (status == ActionStatus.Running) return false;

		var reason = status == ActionStatus.Succeeded ? ActionExitReason.Completed : ActionExitReason.Failed;
		_currentAction.Exit(agent, reason);
		if (status == ActionStatus.Failed) _currentAction.Fail("Plan preempted step due to failure");

		if (status == ActionStatus.Succeeded)
		{
			// Apply effects to tracked facts
			var step = _steps[_currentStepIndex];
			GD.Print($"Step {_currentStepIndex} succeeded, applying {step.Effects.Count} effects");
			foreach (var effect in step.Effects)
			{
				object effectValue = effect.Value;
				if (effect.Value is Func<State, object> func)
				{
					effectValue = func(CurrentState);
				}
				_worldFacts[effect.Key] = effectValue;
			}
			CurrentState = new State(_worldFacts); // Update CurrentState
		}
		else
		{
			GD.PushError($"Plan step {_currentStepIndex} failed - check action Fail logs for reason");
			IsComplete = true;
			Succeeded = false;
			return true;
		}

		_currentAction = null;
		return _currentStepIndex >= _steps.Count - 1; // Continue if more steps
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
		_worldFacts.Clear();
		_worldFacts = new Dictionary<string, object>(CurrentState.Facts); // Reses
	}

	// Helper to check if goal satisfied
	public bool SatisfiesGoal(State goal)
	{
		return CurrentState.Satisfies(goal);
	}
}
