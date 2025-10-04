using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Components;
using Game.Universe;

namespace Game.Utils;

/// <summary>
/// Singleton that builds world state ONCE per frame and shares it across all agents.
/// Prevents every agent from scanning the entire world independently.
/// </summary>
public class GlobalWorldStateManager
{
	private static GlobalWorldStateManager _instance;
	public static GlobalWorldStateManager Instance => _instance ??= new GlobalWorldStateManager();

	private WorldStateData _cachedWorldData;
	private float _lastWorldDataUpdate;
	private const float WORLD_DATA_UPDATE_INTERVAL = 0.5f; // Update world data every 500ms
	
	// Cache enum values to avoid repeated allocations
	private static readonly TargetType[] _cachedTargetTypes = (TargetType[])Enum.GetValues(typeof(TargetType));
	private static readonly string[] _cachedTargetTypeStrings;

	// Static constructor to cache type strings
	static GlobalWorldStateManager()
	{
		_cachedTargetTypeStrings = new string[_cachedTargetTypes.Length];
		for (int i = 0; i < _cachedTargetTypes.Length; i++)
		{
			_cachedTargetTypeStrings[i] = _cachedTargetTypes[i].ToString();
		}
	}

	private GlobalWorldStateManager() { }

	/// <summary>
	/// Gets the cached world state data. Automatically refreshes if expired.
	/// </summary>
	public WorldStateData GetWorldState()
	{
		float currentTime = Time.GetTicksMsec() / 1000.0f;
		
		if (_cachedWorldData == null || currentTime - _lastWorldDataUpdate > WORLD_DATA_UPDATE_INTERVAL)
		{
			UpdateWorldData();
			_lastWorldDataUpdate = currentTime;
		}

		return _cachedWorldData;
	}

	/// <summary>
	/// Forces an immediate refresh of world state data (e.g. after significant world changes)
	/// </summary>
	public void ForceRefresh()
	{
		UpdateWorldData();
		_lastWorldDataUpdate = Time.GetTicksMsec() / 1000.0f;
	}

	private void UpdateWorldData()
	{
		if (_cachedWorldData == null)
		{
			_cachedWorldData = new WorldStateData();
		}

		_cachedWorldData.EntityCounts.Clear();
		_cachedWorldData.AvailabilityFlags.Clear();
		_cachedWorldData.EntityPositions.Clear();

		// OPTIMIZED: Use readonly list to avoid LINQ allocation
		var allEntities = EntityManager.Instance.AllEntities;
		
		// Pre-allocate count arrays
		Span<int> targetCounts = stackalloc int[_cachedTargetTypes.Length];
		int treeCount = 0;
		int positionIndex = 0;

		// SINGLE PASS through all entities
		for (int i = 0; i < allEntities.Count; i++)
		{
			if (allEntities[i] is not Entity entity) continue;
			
			// Check if it's a tree
			bool isTree = entity.Tags.Contains(Tags.Tree);
			if (isTree)
			{
				// Only count/track alive trees
				if (entity.TryGetComponent<HealthComponent>(out var health) && health.IsAlive)
				{
					treeCount++;
					if (entity.TryGetComponent<TransformComponent2D>(out var treeTransform))
					{
						_cachedWorldData.EntityPositions[$"Tree_{positionIndex++}"] = treeTransform.Position;
					}
				}
			}
			// Check if it has a target component
			else if (entity.TryGetComponent<TargetComponent>(out var tc))
			{
				// Count by target type
				for (int t = 0; t < _cachedTargetTypes.Length; t++)
				{
					if (tc.Target == _cachedTargetTypes[t])
					{
						targetCounts[t]++;
						break;
					}
				}
				
				// Cache position
				if (entity.TryGetComponent<TransformComponent2D>(out var transform))
				{
					_cachedWorldData.EntityPositions[$"{tc.Target}_{positionIndex++}"] = transform.Position;
				}
			}
		}

		// Store counts in dictionaries (done once after counting) - use cached strings
		for (int i = 0; i < _cachedTargetTypes.Length; i++)
		{
			var targetName = _cachedTargetTypeStrings[i]; // Use pre-cached string
			int count = targetCounts[i];
			_cachedWorldData.EntityCounts[targetName] = count;
			_cachedWorldData.AvailabilityFlags[$"Available_{targetName}"] = count > 0;
		}

		// Store tree count
		_cachedWorldData.EntityCounts["Tree"] = treeCount;
		_cachedWorldData.AvailabilityFlags["TreeAvailable"] = treeCount > 0;
	}
}

