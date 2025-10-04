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
	private float timer = 0f;

	// Cached world state to avoid expensive rebuilding every frame
	private WorldStateCache _worldStateCache;
	private const float STATE_CACHE_DURATION = 0.1f; // Cache for 100ms

	// State change detection to avoid unnecessary planning
	private Dictionary<string, object> _lastPlanningState;
	private float _lastPlanningTime;
	private const float MIN_PLANNING_INTERVAL = 1.0f; // Don't plan more than once per second
	private bool _forceReplan; // Force immediate replan (bypasses rate limit and state change detection)

	// Failure tracking to prevent tight replan loops
	private int _consecutiveFailures;
	private const int MAX_IMMEDIATE_RETRIES = 2; // After 2 failures, wait before retrying

	// Performance optimizations: cache enum values and string keys to avoid allocations
	private static readonly TargetType[] _cachedTargetTypes = (TargetType[])Enum.GetValues(typeof(TargetType));
	private static readonly string[] _cachedTargetTypeStrings;
	private static readonly Dictionary<string, int> _targetTypeStringToIndex;
	private string _cachedAgentId; // Cache agent ID string to avoid repeated ToString()

	// Static constructor to initialize cached type strings
	static AIGoalExecutor()
	{
		_cachedTargetTypeStrings = new string[_cachedTargetTypes.Length];
		_targetTypeStringToIndex = new Dictionary<string, int>(_cachedTargetTypes.Length);

		for (int i = 0; i < _cachedTargetTypes.Length; i++)
		{
			string typeName = _cachedTargetTypes[i].ToString();
			_cachedTargetTypeStrings[i] = typeName;
			_targetTypeStringToIndex[typeName] = i;
		}
	}

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
		float currentTime = timer / 1000.0f;
		timer += (float)delta;

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
					_consecutiveFailures = 0; // Reset on successful planning
				}
				else
				{
					// Planning succeeded but no plan found - goal is impossible right now
					GD.Print($"[{Entity.Name}] Cannot plan for '{_currentGoal?.Name}' - no valid path to goal");
					var failedGoal = _currentGoal;
					_currentGoal = null;
					_currentPlan = null;
					_consecutiveFailures = 0; // Reset when giving up on goal
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

			// Skip rate limit and state change checks if forced replan (e.g., after plan failure)
			bool shouldPlan = _forceReplan;

			if (!shouldPlan)
			{
				// Rate limit planning
				if (planningTime - _lastPlanningTime < MIN_PLANNING_INTERVAL)
				{
					return;
				}

				// Check if world state has meaningfully changed
				shouldPlan = HasWorldStateChangedSignificantly(currentState.Facts);
			}

			if (shouldPlan)
			{
				_planningInProgress = true;
				_lastPlanningState = new Dictionary<string, object>(currentState.Facts);
				_lastPlanningTime = planningTime;
				_forceReplan = false; // Reset force flag

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
					// Plan succeeded, reset failure counter
					_consecutiveFailures = 0;
					OnPlanSucceeded?.Invoke();
				}
				else
				{
					GD.Print($"[{Entity.Name}] Plan execution failed for '{_currentGoal?.Name}'");
					var failedGoal = _currentGoal;
					_currentPlan = null;
					_worldStateCache = null;
					_consecutiveFailures++;

					// Only force immediate replan for first few failures
					// After that, respect rate limits to avoid tight replan loops
					if (_consecutiveFailures <= MAX_IMMEDIATE_RETRIES)
					{
						_forceReplan = true;
					}
					else
					{
						GD.Print($"[{Entity.Name}] Too many consecutive failures ({_consecutiveFailures}), waiting before retry");
					}

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
			FactKeys.WorldCount(TargetType.Stick),  // Now counts unreserved sticks!
			FactKeys.WorldHas(TargetType.Stick),
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
			AddNPCFacts(facts, worldData, npcData, _cachedTargetTypes, _cachedTargetTypeStrings);

		// Compute proximity facts AND available resource counts in ONE spatial query
		// OPTIMIZED: Single pass provides both proximity and reservation-aware availability
		if (Entity.TryGetComponent<TransformComponent2D>(out var agentTransform))
			AddProximityAndAvailabilityFacts(facts, agentTransform, Entity, _cachedTargetTypes, _cachedTargetTypeStrings);
		

		// Cache the state
		float cacheTime = timer / 1000.0f;
		_worldStateCache = new WorldStateCache(facts, cacheTime);

		return new State(facts);
	}

	private static void AddProximityAndAvailabilityFacts(Dictionary<string, object> facts, TransformComponent2D agentTransform,
		                              Entity agent, TargetType[] cachedTargetTypes, string[] cachedTargetTypeStrings)
	{
		var agentPos = agentTransform.Position;
		const float searchRadius = 5000f; // Match movement action search radius

		// Pre-allocate arrays to track proximity and availability
		Span<bool> hasNearby = stackalloc bool[cachedTargetTypes.Length];
		Span<int> availableCount = stackalloc int[cachedTargetTypes.Length];

		// SINGLE spatial query for both proximity (64 units) and availability (5000 units)
		var nearbyEntities = Universe.EntityManager.Instance?.SpatialPartition?.QueryCircle(agentPos, searchRadius);

		if (nearbyEntities != null)
		{
			var reservationManager = Universe.ResourceReservationManager.Instance;

			// Single pass through entities - compute BOTH proximity AND availability
			foreach (var entity in nearbyEntities)
			{
				float distance = agentPos.DistanceTo(entity.Transform?.Position ?? agentPos);
				bool isNear = distance <= 64f;
				bool isAvailable = reservationManager.IsAvailableFor(entity, agent);

				// Check if entity has a TargetComponent
				if (entity.TryGetComponent<TargetComponent>(out var tc))
				{
					for (int i = 0; i < cachedTargetTypes.Length; i++)
					{
						if (tc.Target == cachedTargetTypes[i])
						{
							if (isNear) hasNearby[i] = true;
							if (isAvailable) availableCount[i]++;
							break;
						}
					}
				}
				// Also check for trees (which don't have TargetComponent)
				else if (entity.Tags.Contains(Data.Tags.Tree))
				{
					// Only count alive trees
					if (entity.TryGetComponent<HealthComponent>(out var health) && health.IsAlive)
					{
						for (int i = 0; i < cachedTargetTypes.Length; i++)
						{
							if (cachedTargetTypes[i] == TargetType.Tree)
							{
								if (isNear) hasNearby[i] = true;
								if (isAvailable) availableCount[i]++;
								break;
							}
						}
					}
				}
			}
		}

		// Set facts based on results
		for (int i = 0; i < cachedTargetTypes.Length; i++)
		{
			facts[FactKeys.NearTarget(cachedTargetTypes[i])] = hasNearby[i];
			facts[FactKeys.WorldCount(cachedTargetTypes[i])] = availableCount[i];
			facts[FactKeys.WorldHas(cachedTargetTypes[i])] = availableCount[i] > 0;
		}
	}

	private static void AddNPCFacts(Dictionary<string, object> facts, WorldStateData worldData, NPCData npcData, TargetType[] cachedTargetTypes, string[] cachedTargetTypeStrings)
	{
		// Add agent inventory facts
		// NOTE: WorldCount/WorldHas are now set by AddProximityAndAvailabilityFacts (reservation-aware)
		for (int i = 0; i < cachedTargetTypes.Length; i++)
		{
			var rt = cachedTargetTypes[i];
			int agentCount = npcData.Resources.GetValueOrDefault(rt, 0);

			facts[FactKeys.AgentCount(rt)] = agentCount;
			facts[FactKeys.AgentHas(rt)] = agentCount > 0;
		}

		// Add hunger to facts for goal evaluation
		facts["Hunger"] = npcData.Hunger;
	}
}
