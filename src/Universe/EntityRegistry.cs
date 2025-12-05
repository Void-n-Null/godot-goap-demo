using System;
using System.Collections.Generic;
using Godot;
using Game.Data;
using Game.Data.Components;
using Game.Utils;

namespace Game.Universe;

/// <summary>
/// Manages entity lifecycle and lookup operations.
/// Extracted from EntityManager to follow Single Responsibility Principle.
/// Handles registration, unregistration, and queries.
/// </summary>
public class EntityRegistry
{
    public EntityRegistry(int maxEntities){
        _maxEntityCount = maxEntities;
        _entities = new(maxEntities);
        _entityLookup = new(maxEntities);
    }
    private int _maxEntityCount;
    
    private readonly ActiveEntityTracker _activeTracker = new();
    private readonly Queue<Action> _pendingActiveListMutations = new();

    private readonly List<IUpdatableEntity> _entities;
	private readonly HashSet<IUpdatableEntity> _entityLookup;
	private readonly Dictionary<Guid, Entity> _entityById = new();

	public IReadOnlyList<IUpdatableEntity> Entities => _entities;
	public IReadOnlyList<IUpdatableEntity> ActiveEntities => _activeTracker.ActiveEntities;
	public int Count => _entities.Count;
	public int ActiveCount => _activeTracker.Count;
	public bool IsFull => Count >= _maxEntityCount;

	public SpatialIndex SpatialPartition
	{
		get
		{
			EnsureSpatialIndex();
			return _spatialIndex;
		}
	}

	// Events for external systems to hook into
	public event Action<Entity> EntityRegistered;
	public event Action<Entity> EntityUnregistered;

	private SpatialIndex _spatialIndex;
	private readonly HashSet<Entity> _spatialDirty = new();

	private void OnEntityPositionChanged(Entity entity)
	{
		if (entity == null) return;
		_spatialDirty.Add(entity);
	}
	
	private void HookActiveTracking(Entity entity) => entity.ActiveComponentsStateChanged += OnActiveComponentsStateChanged;
	private void UnhookActiveTracking(Entity entity) => entity.ActiveComponentsStateChanged -= OnActiveComponentsStateChanged;

	private void OnActiveComponentsStateChanged(Entity entity, bool isActive)
	{
		// Mutating active list is always deferred to be safe during iteration
		lock (_pendingActiveListMutations)
		{
			_pendingActiveListMutations.Enqueue(() => ApplyActiveChange(entity, isActive));
		}
	}

	private void ApplyActiveChange(Entity entity, bool isActive)
	{
		if (isActive)
		{
			if (!_activeTracker.Contains(entity)) _activeTracker.Add(entity);
		}
		else
		{
			_activeTracker.Remove(entity);
		}
	}
	
	public void ProcessPendingActiveMutations()
	{
		lock (_pendingActiveListMutations)
		{
			while (_pendingActiveListMutations.Count > 0)
			{
				var action = _pendingActiveListMutations.Dequeue();
				try { action?.Invoke(); }
				catch (Exception ex) { GD.PushError($"EntityRegistry: Active list mutation failed: {ex}"); }
			}
		}
	}

	public void FlushSpatialUpdates()
	{
		if (_spatialIndex == null || _spatialDirty.Count == 0) return;

		foreach (var entity in _spatialDirty)
		{
			_spatialIndex.Sync(entity);
		}
		_spatialDirty.Clear();
	}

	private void EnsureSpatialIndex()
	{
		if (_spatialIndex != null) return;

		_spatialIndex = new SpatialIndex();
		foreach (var entity in _entities)
		{
			if (entity is Entity concrete)
			{
				_spatialIndex.Sync(concrete);
			}
		}
	}

	/// <summary>
	/// Register a new entity in the registry
	/// </summary>
	public bool Register(IUpdatableEntity entity)
	{
		if (entity == null)
			return false;

		if (_entities.Count >= _maxEntityCount)
		{
			LM.Error($"EntityRegistry: Cannot register entity - max capacity ({_maxEntityCount}) reached");
			return false;
		}

		if (!_entityLookup.Add(entity))
		{
			LM.Warning($"EntityRegistry: Entity {(entity as Entity)?.Name} already registered");
			return false;
		}

		_entities.Add(entity);

		if (entity is Entity e)
		{
			_entityById[e.Id] = e;
			
			// Sync with spatial index
			EnsureSpatialIndex();
			_spatialIndex.Sync(e);
			e.Transform.PositionChanged += OnEntityPositionChanged;
			
			// Seed active list if already active BEFORE hooking events
			if (e.HasActiveComponents && !_activeTracker.Contains(e))
			{
				_activeTracker.Add(e);
			}
			HookActiveTracking(e);
			
			WorldEventBus.Instance.PublishEntitySpawned(e);
			
			EntityRegistered?.Invoke(e);
		}

		return true;
	}

	/// <summary>
	/// Unregister an entity from the registry
	/// </summary>
	public bool Unregister(IUpdatableEntity entity, bool broadcastWorldEvent = false)
	{
		if (entity == null)
			return false;

		if (!_entityLookup.Remove(entity))
			return false;

		_entities.Remove(entity);

		if (entity is Entity e)
		{
			_entityById.Remove(e.Id);
			
			// Remove from spatial index
			if (_spatialIndex != null)
			{
				_spatialIndex.Untrack(e);
			}
			_spatialDirty.Remove(e);
			e.Transform.PositionChanged -= OnEntityPositionChanged;
			
			UnhookActiveTracking(e);
			_activeTracker.Remove(e);
			
			if (broadcastWorldEvent)
			{
				WorldEventBus.Instance.PublishEntityDespawned(e);
			}
			
			EntityUnregistered?.Invoke(e);
		}

		return true;
	}

	/// <summary>
	/// Get entity by ID
	/// </summary>
	public Entity GetById(Guid id)
	{
		return _entityById.TryGetValue(id, out var entity) ? entity : null;
	}

	/// <summary>
	/// Query entities by tag within a radius (spatial)
	/// </summary>
	public IReadOnlyList<Entity> QueryByTag(Tag tag, Vector2 position, float radius, int maxResults = int.MaxValue)
	{
		EnsureSpatialIndex();
		return _spatialIndex.QueryCircle(position, radius, e => e.Tags.Contains(tag), maxResults);
	}

	/// <summary>
	/// Query entities by component within a radius (spatial)
	/// </summary>
	public IReadOnlyList<Entity> QueryByComponent<T>(Vector2 position, float radius, int maxResults = int.MaxValue) where T : class, IComponent
	{
		EnsureSpatialIndex();
		return _spatialIndex.QueryCircle(position, radius, e => e.HasComponent<T>(), maxResults);
	}

	/// <summary>
	/// Query all entities by tag (non-spatial)
	/// </summary>
	public List<Entity> QueryByTag(Tag tag)
	{
		var results = new List<Entity>();
		foreach (var entity in _entities)
		{
			if (entity is Entity e && e.Tags.Contains(tag))
			{
				results.Add(e);
			}
		}
		return results;
	}

	/// <summary>
	/// Query all entities by component type (non-spatial)
	/// </summary>
	public List<Entity> QueryByComponent<T>() where T : class, IComponent
	{
		var results = new List<Entity>();
		foreach (var entity in _entities)
		{
			if (entity is Entity e && e.HasComponent<T>())
			{
				results.Add(e);
			}
		}
		return results;
	}

	/// <summary>
	/// Check if an entity is registered
	/// </summary>
	public bool Contains(IUpdatableEntity entity)
	{
		return _entityLookup.Contains(entity);
	}

	/// <summary>
	/// Clear all entities
	/// </summary>
	public void Clear()
	{
		_entities.Clear();
		_entityLookup.Clear();
		_entityById.Clear();
		_spatialDirty.Clear();
		_activeTracker.Clear();
		_spatialIndex = null; // Recreated on next EnsureSpatialIndex
	}
}
