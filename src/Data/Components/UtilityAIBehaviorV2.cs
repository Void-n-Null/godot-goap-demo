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

namespace Game.Data.Components;

/// <summary>
/// Utility AI system that evaluates multiple goals and pursues the highest-utility one
/// </summary>
public class UtilityAIBehaviorV2 : IActiveComponent
{
	public Entity Entity { get; set; }

	private Plan _currentPlan;
	private Task<Plan> _planningTask;
	private bool _planningInProgress;
	private IUtilityGoal _currentGoal;
	
	// Available goals to choose from
	private List<IUtilityGoal> _availableGoals = new();

	// Cached world state to avoid expensive rebuilding every frame
	private WorldStateCache _worldStateCache;
	private const float STATE_CACHE_DURATION = 0.1f; // Cache for 100ms

	// State change detection to avoid unnecessary planning
	private Dictionary<string, object> _lastPlanningState;
	private float _lastPlanningTime;
	private const float MIN_PLANNING_INTERVAL = 1.0f; // Don't plan more than once per second

	// Pre-computed world data for efficient state building
	private WorldStateData _worldData;
	private float _lastWorldDataUpdate;
	private const float WORLD_DATA_UPDATE_INTERVAL = 0.5f; // Update world data every 500ms
	
	// Goal evaluation
	private float _lastGoalEvaluation;
	private const float GOAL_EVALUATION_INTERVAL = 0.5f; // Re-evaluate goals every 500ms

	public void Update(double delta)
	{
		// Use cached state if available and not expired, otherwise rebuild
		float currentTime = Time.GetTicksMsec() / 1000.0f;

		// Ensure world data is kept fresh
		UpdateWorldDataIfNeeded(currentTime);

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
					GD.Print($"[{Entity.Name}] No plan for '{_currentGoal?.Name}'");
				}
			}
			else if (_planningTask.IsFaulted)
			{
				GD.Print($"[{Entity.Name}] Planning failed for '{_currentGoal?.Name}'");
				_planningInProgress = false;
				_planningTask = null;
			}
		}

		// Evaluate goals periodically
		if (currentTime - _lastGoalEvaluation > GOAL_EVALUATION_INTERVAL)
		{
			_lastGoalEvaluation = currentTime;
			EvaluateAndSelectGoal();
		}

		// Check if current goal is satisfied
		if (_currentGoal != null && _currentGoal.IsSatisfied(Entity))
		{
			GD.Print($"[{Entity.Name}] Goal '{_currentGoal.Name}' satisfied!");
			_currentPlan = null;
			_currentGoal = null;
			EvaluateAndSelectGoal(); // Pick new goal immediately
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
				_planningTask = GOAPlanner.PlanAsync(currentState, goalState);
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
				}
				else
				{
					GD.Print($"[{Entity.Name}] Plan failed for '{_currentGoal?.Name}', replanning...");
					_currentPlan = null;
					_worldStateCache = null;
					_lastWorldDataUpdate = -WORLD_DATA_UPDATE_INTERVAL;
					UpdateWorldData();
				}
			}
		}
	}

	private void EvaluateAndSelectGoal()
	{
		if (_availableGoals.Count == 0) return;

		// Calculate utility for each goal
		var goalUtilities = _availableGoals
			.Select(g => new { Goal = g, Utility = g.CalculateUtility(Entity) })
			.OrderByDescending(x => x.Utility)
			.ToList();

		var bestGoal = goalUtilities.First();

		// Only change goals if utility difference is significant (avoid thrashing)
		if (_currentGoal != bestGoal.Goal && bestGoal.Utility > 0.05f)
		{
			var oldGoal = _currentGoal?.Name ?? "None";
			_currentGoal = bestGoal.Goal;
			GD.Print($"[{Entity.Name}] Goal switch: '{oldGoal}' → '{_currentGoal.Name}' (utility: {bestGoal.Utility:F2})");
			
			// Cancel current plan when goal changes
			_currentPlan = null;
			_worldStateCache = null;
		}
	}

	public void OnStart()
	{
		// Register available goals
		_availableGoals.Add(new EatFoodGoal());
		_availableGoals.Add(new StayWarmGoal());
		_availableGoals.Add(new IdleGoal());

		GD.Print($"[{Entity.Name}] UtilityAI started with {_availableGoals.Count} goals");

		// Seed world data immediately
		_lastWorldDataUpdate = -WORLD_DATA_UPDATE_INTERVAL;
		UpdateWorldDataIfNeeded(Time.GetTicksMsec() / 1000.0f);
		
		// Evaluate goals immediately
		EvaluateAndSelectGoal();
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
		facts[FactKeys.AgentId] = Entity.Id.ToString();

		// Use pre-computed world data
		if (_worldData != null && Entity.TryGetComponent<NPCData>(out var npcData))
		{
			// World facts for each resource type
			foreach (TargetType rt in Enum.GetValues<TargetType>())
			{
				int worldCount = _worldData.EntityCounts.GetValueOrDefault(rt.ToString(), 0);
				int agentCount = npcData.Resources.GetValueOrDefault(rt, 0);
				
				facts[FactKeys.WorldCount(rt)] = worldCount;
				facts[FactKeys.WorldHas(rt)] = worldCount > 0;
				facts[FactKeys.AgentCount(rt)] = agentCount;
				facts[FactKeys.AgentHas(rt)] = agentCount > 0;
			}
			
			// Add hunger to facts for goal evaluation
			facts["Hunger"] = npcData.Hunger;
		}

		// Compute proximity facts
		if (Entity.TryGetComponent<TransformComponent2D>(out var agentTransform) && _worldData != null)
		{
			const float nearRadius = 64f;

			foreach (TargetType targetType in Enum.GetValues<TargetType>())
			{
				var nearbyCount = _worldData.EntityPositions
					.Where(kvp => kvp.Key.StartsWith($"{targetType}_"))
					.Count(kvp => agentTransform.Position.DistanceTo(kvp.Value) <= nearRadius);
				
				facts[FactKeys.NearTarget(targetType)] = nearbyCount > 0;
			}
		}

		// Cache the state
		float cacheTime = Time.GetTicksMsec() / 1000.0f;
		_worldStateCache = new WorldStateCache(facts, cacheTime);

		return new State(facts);
	}

	private void UpdateWorldDataIfNeeded(float currentTime)
	{
		if (_worldData == null || currentTime - _lastWorldDataUpdate > WORLD_DATA_UPDATE_INTERVAL)
		{
			UpdateWorldData();
			_lastWorldDataUpdate = currentTime;
		}
	}

	private void UpdateWorldData()
	{
		if (_worldData == null)
		{
			_worldData = new WorldStateData();
		}

		_worldData.EntityCounts.Clear();
		_worldData.AvailabilityFlags.Clear();
		_worldData.EntityPositions.Clear();

		var allEntities = EntityManager.Instance.AllEntities.OfType<Entity>().ToList();

		// Count entities by type
		if (Entity.TryGetComponent<NPCData>(out var npcData))
		{
			foreach (TargetType rt in Enum.GetValues<TargetType>())
			{
				var targetName = rt.ToString();
				var count = allEntities.Count(e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == rt);
				_worldData.EntityCounts[targetName] = count;
				_worldData.AvailabilityFlags[$"Available_{targetName}"] = count > 0;
			}
		}

		// Count trees - ONLY ALIVE ones
		var treeCount = allEntities.Count(e => e.Tags.Contains(Tags.Tree) 
			&& e.TryGetComponent<HealthComponent>(out var health) 
			&& health.IsAlive);
		_worldData.EntityCounts["Tree"] = treeCount;
		_worldData.AvailabilityFlags["TreeAvailable"] = treeCount > 0;

		// Cache positions for proximity - ONLY ALIVE entities
		for (int i = 0; i < allEntities.Count; i++)
		{
			var entity = allEntities[i];
			if (entity.TryGetComponent<TransformComponent2D>(out var transform))
			{
				if (entity.Tags.Contains(Tags.Tree))
				{
					if (entity.TryGetComponent<HealthComponent>(out var health) && health.IsAlive)
					{
						_worldData.EntityPositions[$"Tree_{i}"] = transform.Position;
					}
				}
				else if (entity.TryGetComponent<TargetComponent>(out var tc))
				{
					_worldData.EntityPositions[$"{tc.Target}_{i}"] = transform.Position;
				}
			}
		}
	}
}
