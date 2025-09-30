using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.GOAP;
using Game.Data.Components;
using Game.Universe;
using System.Reflection;
using System.Threading.Tasks;

namespace Game.Data.Components;
// Pre-computed world state data for efficient planning
public class WorldStateData
{
	public Dictionary<string, int> EntityCounts { get; } = new();
	public Dictionary<string, bool> AvailabilityFlags { get; } = new();
	public Dictionary<string, Vector2> EntityPositions { get; } = new();

	public WorldStateData()
	{
	}
}

// Cache for expensive world state calculations
public class WorldStateCache
{
	public Dictionary<string, object> CachedFacts { get; private set; }
	public float CacheTime { get; private set; }

	public WorldStateCache(Dictionary<string, object> facts, float currentTime)
	{
		CachedFacts = new Dictionary<string, object>(facts);
		CacheTime = currentTime;
	}

	public bool IsExpired(float currentTime, float maxAge)
	{
		return currentTime - CacheTime > maxAge;
	}
}


public class UtilityAIBehavior : IActiveComponent
{
	public Entity Entity { get; set; }

	private Plan _currentPlan;
	private Task<Plan> _planningTask;
	private bool _planningInProgress;
	private State _goalState;
	private Dictionary<string, object> _trackedFacts = new(); // Local mutable facts for goal checking

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

	public void Update(double delta)
	{
		// Use cached state if available and not expired, otherwise rebuild
		float currentTime = Time.GetTicksMsec() / 1000.0f;

		// Ensure world data is kept fresh
		UpdateWorldDataIfNeeded(currentTime);

		// Use cached state unless plan is executing; build fresh when needed
		var ctx = (_currentPlan == null || _currentPlan.IsComplete) && (_worldStateCache != null && !_worldStateCache.IsExpired(currentTime, STATE_CACHE_DURATION))
			? new State(_worldStateCache.CachedFacts) { Agent = Entity, World = new World { EntityManager = EntityManager.Instance, GameManager = GameManager.Instance } }
			: BuildCurrentState();

		// Update tracked facts from ctx
		foreach (var fact in ctx.Facts)
		{
			_trackedFacts[fact.Key] = fact.Value;
		}

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
					GD.Print("HERES MY PLAN:");
					for (int i = 0; i < _currentPlan.Steps.Count; i++)
					{
						var step = _currentPlan.Steps[i];
						var action = step.CreateAction(ctx); // Create action to get type name
						var stepNum = i + 1;
						if (stepNum == 1)
						{
							GD.Print($"- First I'm Gonna {action.GetType().Name}");
						}
						else if (stepNum == _currentPlan.Steps.Count)
						{
							GD.Print($"- Finally, {action.GetType().Name}");
						}
						else
						{
							GD.Print($"- Then, {action.GetType().Name}");
						}
					}
					GD.Print($"Started new plan to get wood: {_currentPlan.Steps.Count} steps");
				}
				else
				{
					GD.Print("No plan found to get sticks, trying again later...");
				}
			}
			else if (_planningTask.IsFaulted)
			{
				GD.Print($"Planning failed: {_planningTask.Exception?.GetBaseException().Message}");
				_planningInProgress = false;
				_planningTask = null;
			}
			// If still running, do nothing and check next frame
		}

		if (_currentPlan == null || _currentPlan.IsComplete)
		{
			if (_currentPlan?.Succeeded == true)
			{
				// Celebrate on success
				if (ctx.Satisfies(_goalState))
				{
					GD.Print("Yay! Collected 12 sticks! 🎉 Wood gathering complete!");
					GD.Print("The forest is now my stick supplier! 🪵🔥");
					GD.Print("Time to craft something epic! 🏹");
				}
				_currentPlan = null;
			}

			// Replan if goal not met and not already planning
			if (!_HasEnoughSticks() && !_planningInProgress)
			{
				float planningTime = Time.GetTicksMsec() / 1000.0f;

				// Rate limit planning to avoid excessive calls
				if (planningTime - _lastPlanningTime < MIN_PLANNING_INTERVAL)
				{
					return;
				}

				// Check if world state has meaningfully changed
				if (HasWorldStateChangedSignificantly(ctx.Facts))
				{
					_planningInProgress = true;
					_lastPlanningState = new Dictionary<string, object>();
					foreach (var fact in ctx.Facts)
					{
						_lastPlanningState[fact.Key] = fact.Value;
					}
					_lastPlanningTime = planningTime;
					_planningTask = GOAPlanner.PlanAsync(ctx, _goalState);
				}
			}
			else if (_HasEnoughSticks())
			{
				GD.Print("Already have 12 sticks, relaxing... 😊");
			}
		}

		if (_currentPlan != null && !_currentPlan.IsComplete)
		{
			var tickResult = _currentPlan.Tick(ctx, (float)delta);
			_trackedFacts = new Dictionary<string, object>(_currentPlan.CurrentState.Facts); // Sync after every tick

			// Clear world state cache when plan makes changes to ensure fresh state building
			if (tickResult && _currentPlan.Succeeded)
			{
				_worldStateCache = null; // Force fresh state building next frame
			}

			if (tickResult)
			{
				if (_currentPlan.Succeeded)
				{
					// Plan done, will celebrate in next update
				}
				else
				{
					GD.Print("Plan failed, replanning...");
					_currentPlan = null;
					_worldStateCache = null; // Clear cache on failure too
				}
			}
		}
	}

	public void OnStart()
	{
		_goalState = new State(new Dictionary<string, object> { {"StickCount", 12} });
		_trackedFacts["NeedsToApproach"] = true; // Initial fact
		GD.Print("UtilityAIBehavior started - goal: collect 12 sticks!");

		// Seed world data immediately to avoid null world facts on first few frames
		_lastWorldDataUpdate = -WORLD_DATA_UPDATE_INTERVAL; // force refresh
		UpdateWorldDataIfNeeded(Time.GetTicksMsec() / 1000.0f);
	}

	public void OnPostAttached()
	{
		// No-op
	}

	private bool _HasEnoughSticks()
	{
		return _trackedFacts.TryGetValue("StickCount", out var value) && value is int count && count >= 12;
	}

	private bool HasWorldStateChangedSignificantly(IReadOnlyDictionary<string, object> currentFacts)
	{
		if (_lastPlanningState == null)
		{
			return true; // First time planning, always significant
		}

		// Check key facts that would affect planning decisions
		var keyFacts = new[] {
			"StickCount", "TreeCount", "TreeAvailable", "AtTree", "AtStick",
			"WorldStickCount", "Available_Stick", "Available_Tree"
		};

		foreach (var key in keyFacts)
		{
			var currentValue = currentFacts.GetValueOrDefault(key);
			var lastValue = _lastPlanningState.GetValueOrDefault(key);

			if (!object.Equals(currentValue, lastValue))
			{
				GD.Print($"State changed for {key}: {lastValue} -> {currentValue}");
				return true;
			}
		}

		return false; // No significant changes
	}

	private State BuildCurrentState()
	{
		var facts = new Dictionary<string, object>(_trackedFacts); // Start with tracked
		// Add current position, etc., if needed for actions
		if (Entity.TryGetComponent<TransformComponent2D>(out var transform))
		{
			facts["Position"] = transform.Position;
		}
		facts["AgentId"] = Entity.Id.ToString();

		// Use pre-computed world data instead of expensive queries
		if (_worldData != null)
		{
			if (Entity.TryGetComponent<NPCData>(out var npcData))
			{
				foreach (TargetType rt in Enum.GetValues<TargetType>())
				{
					var targetName = rt.ToString();
					facts[$"World{targetName}Count"] = _worldData.EntityCounts.GetValueOrDefault(targetName, 0);
					facts[$"Available_{targetName}"] = _worldData.EntityCounts.GetValueOrDefault(targetName, 0) > 0;
					facts[$"{targetName}Count"] = npcData.Resources.GetValueOrDefault(rt, 0);
				}
			}

			facts["TreeCount"] = _worldData.EntityCounts.GetValueOrDefault("Tree", 0);
			facts["TreeAvailable"] = _worldData.EntityCounts.GetValueOrDefault("Tree", 0) > 0;
		}
		else { /* no-op when world data isn't ready */ }

		// Compute proximity facts using cached positions
		if (Entity.TryGetComponent<TransformComponent2D>(out var agentTransform) && _worldData != null)
		{
			const float atRadius = 64f;

			// Check proximity to trees
			var treesNearby = _worldData.EntityPositions
				.Where(kvp => kvp.Key.StartsWith("Tree_"))
				.Count(kvp => agentTransform.Position.DistanceTo(kvp.Value) <= atRadius);
			facts["AtTree"] = treesNearby > 0;

			// Check proximity to sticks
			var sticksNearby = _worldData.EntityPositions
				.Where(kvp => kvp.Key.StartsWith("Stick_"))
				.Count(kvp => agentTransform.Position.DistanceTo(kvp.Value) <= atRadius);
			facts["AtStick"] = sticksNearby > 0;
		}

		// Cache the expensive state building
		float cacheTime = Time.GetTicksMsec() / 1000.0f;
		_worldStateCache = new WorldStateCache(facts, cacheTime);

		var state = new State(facts);
		state.Agent = Entity;
		state.World = new World
		{
			EntityManager = EntityManager.Instance,
			GameManager = GameManager.Instance
		};
		return state;
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

		// Clear existing data
		_worldData.EntityCounts.Clear();
		_worldData.AvailabilityFlags.Clear();
		_worldData.EntityPositions.Clear();

		// Efficiently compute world state using spatial index
		var allEntities = EntityManager.Instance.AllEntities.OfType<Entity>().ToList();
		// no noisy logging here

		// Count entities by type using efficient lookups
		if (Entity.TryGetComponent<NPCData>(out var npcData))
		{
			foreach (TargetType rt in Enum.GetValues<TargetType>())
			{
				var targetName = rt.ToString();
				var count = allEntities.Count(e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == rt);
				_worldData.EntityCounts[targetName] = count;
				_worldData.AvailabilityFlags[$"Available_{targetName}"] = count > 0;
				// silence noisy per-type logging
			}
		}

		// Count trees
		var treeCount = allEntities.Count(e => e.Tags.Contains(Tags.Tree));
		_worldData.EntityCounts["Tree"] = treeCount;
		_worldData.AvailabilityFlags["TreeAvailable"] = treeCount > 0;
		// silence noisy tree logging

		// Cache positions for proximity calculations
		for (int i = 0; i < allEntities.Count; i++)
		{
			var entity = allEntities[i];
			if (entity.TryGetComponent<TransformComponent2D>(out var transform))
			{
				if (entity.Tags.Contains(Tags.Tree))
				{
					_worldData.EntityPositions[$"Tree_{i}"] = transform.Position;
				}
				else if (entity.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Stick)
				{
					_worldData.EntityPositions[$"Stick_{i}"] = transform.Position;
				}
			}
		}
	}
}
