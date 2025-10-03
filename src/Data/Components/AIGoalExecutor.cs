using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.GOAP;
using Game.Data.Components;
using Game.Universe;
using System.Threading.Tasks;
using Game.Data.UtilityAI;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Executes plans to achieve goals. Fires events to notify selector of status changes.
/// </summary>
public class AIGoalExecutor : IActiveComponent
{
	public Entity Entity { get; set; }

	private Plan _currentPlan;
	private Task<Plan> _planningTask;
	private bool _planningInProgress;
	private IUtilityGoal _currentGoal;

	// Cached world state to avoid expensive rebuilding every frame
	private WorldStateCache _worldStateCache;
	private const float STATE_CACHE_DURATION = 0.1f; // Cache for 100ms

	// State change detection to avoid unnecessary planning
	private Dictionary<string, object> _lastPlanningState;
	private float _lastPlanningTime;
	private const float MIN_PLANNING_INTERVAL = 1.0f; // Don't plan more than once per second

	// Performance optimizations: cache enum values and string keys to avoid allocations
	private static readonly TargetType[] _cachedTargetTypes = (TargetType[])Enum.GetValues(typeof(TargetType));
	private static readonly float _nearRadiusSquared = 64f * 64f; // Cache squared distance for faster proximity checks
	private string _cachedAgentId; // Cache agent ID string to avoid repeated ToString()

	// Events for the selector to listen to
	public event Action<IUtilityGoal> OnCannotPlan; // "I couldn't even find a plan for this goal"
	public event Action<IUtilityGoal> OnPlanExecutionFailed; // "I found a plan but it didn't work when I tried"
	public event Action OnPlanSucceeded;
	public event Action OnGoalSatisfied;
	public event Action OnNeedNewGoal;

	public void SetGoal(IUtilityGoal goal)
	{
		if (_currentGoal != goal)
		{
			_currentGoal = goal;
			// Cancel current plan when goal changes
			_currentPlan = null;
			_worldStateCache = null;
		}
	}

	public void Update(double delta)
	{
		// Use cached state if available and not expired, otherwise rebuild
		float currentTime = Time.GetTicksMsec() / 1000.0f;

		// Build current state (pure facts only, no Agent/World)
		var currentState = (_worldStateCache != null && !_worldStateCache.IsExpired(currentTime, STATE_CACHE_DURATION))
			? new State(_worldStateCache.CachedFacts)
			: BuildCurrentState();

		// Handle async planning completion
		if (_planningInProgress && _planningTask != null)
		{
			if (_planningTask.IsCompletedSuccessfully)
			{
				_currentPlan = _planningTask.Result;
				_planningInProgress = false;
				_planningTask = null;

				if (_currentPlan != null && _currentPlan.Steps.Count > 0)
				{
					GD.Print($"[{Entity.Name}] New plan for '{_currentGoal?.Name}': {_currentPlan.Steps.Count} steps");
				}
				else
				{
					// Planning succeeded but no plan found - goal is impossible right now
					GD.Print($"[{Entity.Name}] Cannot plan for '{_currentGoal?.Name}' - no valid path to goal");
					var failedGoal = _currentGoal;
					_currentGoal = null;
					_currentPlan = null;
					OnCannotPlan?.Invoke(failedGoal);
					return;
				}
			}
			else if (_planningTask.IsFaulted)
			{
				GD.Print($"[{Entity.Name}] Planning error for '{_currentGoal?.Name}': {_planningTask.Exception?.Message}");
				var failedGoal = _currentGoal;
				_currentGoal = null;
				_planningInProgress = false;
				_planningTask = null;
				OnCannotPlan?.Invoke(failedGoal);
				return;
			}
		}

		// Check if current goal is satisfied
		if (_currentGoal != null && _currentGoal.IsSatisfied(Entity))
		{
			_currentPlan = null;
			_currentGoal = null;
			OnGoalSatisfied?.Invoke();
			return;
		}

		// Request new goal if we don't have one
		if (_currentGoal == null)
		{
			OnNeedNewGoal?.Invoke();
			return;
		}

		// Replan if no plan and have a goal
		if ((_currentPlan == null || _currentPlan.IsComplete) && _currentGoal != null && !_planningInProgress)
		{
			if (_currentPlan?.Succeeded == true)
			{
				_currentPlan = null;
			}

			float planningTime = Time.GetTicksMsec() / 1000.0f;

			// Rate limit planning
			if (planningTime - _lastPlanningTime < MIN_PLANNING_INTERVAL)
			{
				return;
			}

			// Check if world state has meaningfully changed
			if (HasWorldStateChangedSignificantly(currentState.Facts))
			{
				_planningInProgress = true;
				_lastPlanningState = new Dictionary<string, object>(currentState.Facts);
				_lastPlanningTime = planningTime;
				
				var goalState = _currentGoal.GetGoalState(Entity);
				_planningTask = AdvancedGoalPlanner.ForwardPlanAsync(currentState, goalState);
			}
		}

		// Execute current plan
		if (_currentPlan != null && !_currentPlan.IsComplete)
		{
			var tickResult = _currentPlan.Tick(Entity, (float)delta);

			// Clear world state cache when plan makes changes
			if (tickResult && _currentPlan.Succeeded)
			{
				_worldStateCache = null;
			}

			if (tickResult)
			{
				if (_currentPlan.Succeeded)
				{
					// Plan succeeded, goal should be satisfied next eval
					OnPlanSucceeded?.Invoke();
				}
				else
				{
					GD.Print($"[{Entity.Name}] Plan execution failed for '{_currentGoal?.Name}'");
					var failedGoal = _currentGoal;
					_currentPlan = null;
					_worldStateCache = null;
					GlobalWorldStateManager.Instance.ForceRefresh();
					OnPlanExecutionFailed?.Invoke(failedGoal);
				}
			}
		}
	}

	public void OnStart()
	{
		// Cache agent ID string once to avoid repeated ToString() calls
		_cachedAgentId = Entity.Id.ToString();
	}

	public void OnPostAttached()
	{
		// No-op
	}

	private bool HasWorldStateChangedSignificantly(IReadOnlyDictionary<string, object> currentFacts)
	{
		if (_lastPlanningState == null)
			return true;

		// Check key facts that would affect planning decisions
		var keyFacts = new[] {
			FactKeys.AgentCount(TargetType.Stick),
			FactKeys.WorldCount(TargetType.Tree),
			FactKeys.WorldHas(TargetType.Tree),
			FactKeys.WorldCount(TargetType.Food),
			FactKeys.WorldHas(TargetType.Food),
			"Hunger"
		};

		foreach (var key in keyFacts)
		{
			var currentValue = currentFacts.GetValueOrDefault(key);
			var lastValue = _lastPlanningState.GetValueOrDefault(key);

			if (!object.Equals(currentValue, lastValue))
			{
				return true;
			}
		}

		return false;
	}

	private State BuildCurrentState()
	{
		var facts = new Dictionary<string, object>();
		
		// Add agent metadata
		if (Entity.TryGetComponent<TransformComponent2D>(out var transform))
		{
			facts[FactKeys.Position] = transform.Position;
		}
		facts[FactKeys.AgentId] = _cachedAgentId;

		// Get SHARED world data (computed once globally, not per-agent!)
		var worldData = GlobalWorldStateManager.Instance.GetWorldState();
		
		if (Entity.TryGetComponent<NPCData>(out var npcData))
		{
			// World facts for each resource type - use cached enum array
			for (int i = 0; i < _cachedTargetTypes.Length; i++)
			{
				var rt = _cachedTargetTypes[i];
				string resourceName = rt.ToString(); // Still need ToString for lookup, but only once per type
				
				int worldCount = worldData.EntityCounts.GetValueOrDefault(resourceName, 0);
				int agentCount = npcData.Resources.GetValueOrDefault(rt, 0);
				
				facts[FactKeys.WorldCount(rt)] = worldCount;
				facts[FactKeys.WorldHas(rt)] = worldCount > 0;
				facts[FactKeys.AgentCount(rt)] = agentCount;
				facts[FactKeys.AgentHas(rt)] = agentCount > 0;
			}
			
			// Add hunger to facts for goal evaluation
			facts["Hunger"] = npcData.Hunger;
		}

		// Compute proximity facts (agent-specific based on their position)
		// OPTIMIZED: Use squared distance and avoid LINQ
		if (Entity.TryGetComponent<TransformComponent2D>(out var agentTransform))
		{
			var agentPos = agentTransform.Position;
			
			// Pre-allocate array to track which target types have nearby entities
			Span<bool> hasNearby = stackalloc bool[_cachedTargetTypes.Length];
			
			// Single pass through all entity positions
			foreach (var kvp in worldData.EntityPositions)
			{
				// Fast squared distance check first (avoids sqrt)
				var dx = kvp.Value.X - agentPos.X;
				var dy = kvp.Value.Y - agentPos.Y;
				var distSquared = dx * dx + dy * dy;
				
				if (distSquared <= _nearRadiusSquared)
				{
					// Entity is nearby - determine which type it is
					// Key format is "{TargetType}_{index}"
					var key = kvp.Key;
					int underscoreIndex = key.IndexOf('_');
					if (underscoreIndex > 0)
					{
						var typePrefix = key.Substring(0, underscoreIndex);
						
						// Match against our cached types
						for (int i = 0; i < _cachedTargetTypes.Length; i++)
						{
							if (typePrefix == _cachedTargetTypes[i].ToString())
							{
								hasNearby[i] = true;
								break;
							}
						}
					}
				}
			}
			
			// Set facts based on proximity results
			for (int i = 0; i < _cachedTargetTypes.Length; i++)
			{
				facts[FactKeys.NearTarget(_cachedTargetTypes[i])] = hasNearby[i];
			}
		}

		// Cache the state
		float cacheTime = Time.GetTicksMsec() / 1000.0f;
		_worldStateCache = new WorldStateCache(facts, cacheTime);

		return new State(facts);
	}
}
