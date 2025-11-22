using Godot;
using System;
using System.Collections.Generic;
using Game;
using Game.Data;
using Game.Data.Components;
using TG = Game.Data.Tags;

namespace Game.Universe;

/// <summary>
/// Flexible entity manager that supports entities with and without views.
/// Uses efficient array-based storage instead of dictionaries for performance.
/// </summary>
public partial class EntityManager : Utils.SingletonNode<EntityManager>
{
	[Export]
	public Node ViewRoot { get; set; }
	[Export]
	public PackedScene DefaultViewScene { get; set; }
	[Export]
	public bool DebugDrawSpatialIndex { get; set; } = false;
	[Export]
	public int MaxEntitiesPerFrame { get; set; } = 1000; // Max entities to update per frame
	[Export]
	public int MaxFramesPerTick { get; set; } = 10; // Max frames to spread a tick across
	/// <summary>
	/// Maximum entities to prevent memory issues.
	/// </summary>
	private const int MAX_ENTITIES = 500000;

	public bool IsFull => _entities.Count >= MAX_ENTITIES;

	/// <summary>
	/// All entities (with or without views) for bulk updates.
	/// Fast array iteration, no dictionary overhead.
	/// </summary>
	private readonly List<IUpdatableEntity> _entities = new(MAX_ENTITIES);
	private readonly HashSet<IUpdatableEntity> _entityLookup = new(MAX_ENTITIES);
	private readonly List<IUpdatableEntity> _activeEntities = new(MAX_ENTITIES);
	private SpatialIndex _spatialIndex;
	private readonly HashSet<Entity> _spatialDirty = new();
	private readonly HashSet<Entity> _spatialTracked = new();
	private bool _isProcessingTick;
	private readonly Queue<Action> _pendingEntityListMutations = new();
	private readonly Dictionary<Guid, Entity> _entityById = new();

	// Tick slicing state
	private sealed class TickWorkItem
	{
		public double Delta { get; }
		public int StartIndex { get; set; }
		public int TotalEntities { get; }

		public TickWorkItem(double delta, int totalEntities)
		{
			Delta = delta;
			TotalEntities = totalEntities;
			StartIndex = 0;
		}

		public bool IsComplete => StartIndex >= TotalEntities;
	}

	private TickWorkItem _currentTickWork;

	public SpatialIndex SpatialPartition
	{
		get
		{
			EnsureSpatialIndex();
			return _spatialIndex;
		}
	}

	public IReadOnlyList<IUpdatableEntity> AllEntities => _entities.AsReadOnly();

	// Intentionally no view mappings. Views are owned by VisualComponent.

	public override void _Ready()
	{
		GD.Print("EntityManager: Ready");
		base._Ready();

		EnsureSpatialIndex();
		// Subscribe to GameManager's efficient tick system
		GameManager.Instance.SubscribeToTick(OnTick);

		// Set global view defaults if provided
		if (ViewRoot != null)
		{
			Utils.ViewContext.DefaultParent = ViewRoot;
		}

		if (DefaultViewScene != null)
		{
			Utils.ViewContext.DefaultViewScene = DefaultViewScene;
		}




		// Ensure a CustomEntityRenderEngine exists for immediate-mode rendering
		if (Utils.CustomEntityRenderEngineLocator.Renderer == null)
		{
			var ysorted = new Utils.CustomEntityRenderEngine();
			if (ViewRoot is Node2D vr2b)
				vr2b.AddChild(ysorted);
			else
				AddChild(ysorted);
		}
	}

	/// <summary>
	/// Queues up a new tick for processing. The actual entity updates are spread across frames in _Process.
	/// Death spiral protection: if we're still processing the previous tick, skip this one.
	/// </summary>
	private void OnTick(double delta)
	{
		// Death spiral protection: if still processing previous tick, skip this one
		if (_currentTickWork != null && !_currentTickWork.IsComplete)
		{
			GD.PushWarning($"EntityManager: Skipping tick - still processing previous tick ({_currentTickWork.StartIndex}/{_currentTickWork.TotalEntities} entities updated)");
			return;
		}

		// Determine which list to process
		var entitiesToUpdate = _activeEntities.Count > 0 ? _activeEntities : _entities;

		// Queue up new tick work
		_currentTickWork = new TickWorkItem(delta, entitiesToUpdate.Count);
	}

	/// <summary>
	/// Processes entity updates in slices across frames for better performance distribution.
	/// </summary>
	public override void _Process(double delta)
	{
		base._Process(delta);

		// Process a slice of the current tick if there's work to do
		if (_currentTickWork != null && !_currentTickWork.IsComplete)
		{
			ProcessTickSlice();
		}
	}

	/// <summary>
	/// Processes a slice of entities for the current tick.
	/// </summary>
	private void ProcessTickSlice()
	{
		if (_currentTickWork == null || _currentTickWork.IsComplete)
		{
			return;
		}

		EnsureSpatialIndex();
		_isProcessingTick = true;
		try
		{
			var entitiesToUpdate = _activeEntities.Count > 0 ? _activeEntities : _entities;

			// Calculate how many entities to process this frame
			int remainingEntities = _currentTickWork.TotalEntities - _currentTickWork.StartIndex;
			int entitiesToProcess = Math.Min(MaxEntitiesPerFrame, remainingEntities);
			int endIndex = _currentTickWork.StartIndex + entitiesToProcess;

			// Process the slice
			for (int i = _currentTickWork.StartIndex; i < endIndex; i++)
			{
				if (i < entitiesToUpdate.Count) // Safety check
				{
					entitiesToUpdate[i].Update(_currentTickWork.Delta);
				}
			}

			// Update progress
			_currentTickWork.StartIndex = endIndex;

			// If complete, clear the work
			if (_currentTickWork.IsComplete)
			{
				_currentTickWork = null;
			}
		}
		finally
		{
			FlushSpatialUpdates();
			FlushPendingEntityListMutations();
			if (DebugDrawSpatialIndex)
			{
				DrawSpatialDebug();
			}
			_isProcessingTick = false;
		}
	}

	private void DrawSpatialDebug()
	{
		if (_spatialIndex == null) return;
		var renderer = Utils.CustomEntityRenderEngineLocator.Renderer;
		if (renderer == null) return;

		// Draw node loose bounds with subtle color intensity by depth; and tight bounds overlayed
		_spatialIndex.ForEachNode((bounds, loose, depth, count) =>
		{
			var looseColor = new Color(0f, 0.75f, 1f, Mathf.Clamp(0.1f + depth * 0.05f, 0.1f, 0.6f));
			var tightColor = new Color(0f, 1f, 0.3f, Mathf.Clamp(0.15f + depth * 0.05f, 0.15f, 0.8f));

			var l = loose;
			var t = bounds;
			// Loose rectangle
			renderer.QueueDebugLine(l.Position, l.Position + new Vector2(l.Size.X, 0f), looseColor, 7f);
			renderer.QueueDebugLine(l.Position, l.Position + new Vector2(0f, l.Size.Y), looseColor, 7f);
			renderer.QueueDebugLine(l.Position + l.Size, l.Position + new Vector2(0f, l.Size.Y), looseColor, 7f);
			renderer.QueueDebugLine(l.Position + l.Size, l.Position + new Vector2(l.Size.X, 0f), looseColor, 7f);

			// Tight rectangle
			renderer.QueueDebugLine(t.Position, t.Position + new Vector2(t.Size.X, 0f), tightColor, 14f);
			renderer.QueueDebugLine(t.Position, t.Position + new Vector2(0f, t.Size.Y), tightColor, 14f);
			renderer.QueueDebugLine(t.Position + t.Size, t.Position + new Vector2(0f, t.Size.Y), tightColor, 14f);
			renderer.QueueDebugLine(t.Position + t.Size, t.Position + new Vector2(t.Size.X, 0f), tightColor, 14f);
		}, includeEmpty: false);

		// Optionally draw a small circle for each tracked item (cap for perf)
		_spatialIndex.ForEachItem(pos =>
		{
			renderer.QueueDebugCircle(pos, 6f, new Color(1f, 0.6f, 0f, 0.6f), 2f, 20);
		}, maxCount: 2000);
	}

	/// <summary>
	/// Registers any entity for bulk updates.
	/// Works for both view-based and view-less entities.
	/// </summary>
	public bool RegisterEntity(IUpdatableEntity entity)
	{
		if (entity == null)
		{
			return false;
		}

		if (_entities.Count >= MAX_ENTITIES)
		{
			GD.PushWarning($"EntityManager: Max entities ({MAX_ENTITIES}) reached");
			return false;
		}

		if (!_entityLookup.Add(entity))
		{
			return false;
		}

		_entities.Add(entity);
		if (entity is Entity concrete)
		{
			_entityById[concrete.Id] = concrete;
			EnsureSpatialIndex();
			_spatialIndex.Sync(concrete);
			HookSpatialTracking(concrete);
			// Seed active list if already active BEFORE hooking events
			// to avoid double-addition when the event fires
			if (concrete.HasActiveComponents && !_activeEntities.Contains(concrete))
			{
				_activeEntities.Add(concrete);
			}
			HookActiveTracking(concrete);
			WorldEventBus.Instance.PublishEntitySpawned(concrete);
		}
		return true;
	}

	/// <summary>
	/// Registers an entity with a visual view component.
	/// </summary>
	// No RegisterEntityWithView; VisualComponent attaches its own ViewNode.

	/// <summary>
	/// Unregisters an entity from updates.
	/// </summary>
	public bool UnregisterEntity(IUpdatableEntity entity)
	{
		if (entity == null)
		{
			return false;
		}

		if (_entities.Remove(entity))
		{
			_entityLookup.Remove(entity);
			if (entity is Entity concrete)
			{
				_entityById.Remove(concrete.Id);
				EnsureSpatialIndex();
				_spatialIndex.Untrack(concrete);
				_spatialDirty.Remove(concrete);
				UnhookSpatialTracking(concrete);
				UnhookActiveTracking(concrete);
				_activeEntities.Remove(concrete);
				WorldEventBus.Instance.PublishEntityDespawned(concrete);
			}
			return true;
		}
		return false;
	}

	/// <summary>
	/// Gets all entities (for iteration if needed).
	/// </summary>
	public IReadOnlyList<IUpdatableEntity> GetEntities() => _entities;

	/// <summary>
	/// Gets an entity by its unique ID for efficient lookup.
	/// </summary>
	public Entity GetEntityById(Guid id)
	{
		_entityById.TryGetValue(id, out var entity);
		return entity;
	}

	public IReadOnlyList<Entity> QueryByTag(Tag tag, Vector2 position, float radius, int maxResults = int.MaxValue)
	{
		EnsureSpatialIndex();
		return _spatialIndex.QueryCircle(position, radius, e => e.Tags.Contains(tag), maxResults);
	}

	public IReadOnlyList<Entity> QueryByComponent<T>(Vector2 position, float radius, int maxResults = int.MaxValue) where T : class, IComponent
	{
		EnsureSpatialIndex();
		return _spatialIndex.QueryCircle(position, radius, e => e.GetComponent<T>() != null, maxResults);
	}

	public int EntityCount => _entities.Count;
	// No ViewCount here.

	public int ActiveEntityCount
	{
		get
		{
			if (_activeEntities.Count > 0)
			{
				return _activeEntities.Count;
			}
			// If active list is empty, compute actual active Entities
			int count = 0;
			for (int i = 0; i < _entities.Count; i++)
			{
				if (_entities[i] is Entity e && e.HasActiveComponents) count++;
			}
			return count;
		}
	}

	private void MarkSpatialDirty(Entity entity)
	{
		if (entity == null)
		{
			return;
		}

		EnsureSpatialIndex();
		_spatialDirty.Add(entity);

		if (!_isProcessingTick)
		{
			FlushSpatialUpdates();
		}
	}

	private void FlushSpatialUpdates()
	{
		if (_spatialIndex == null || _spatialDirty.Count == 0)
		{
			return;
		}

		foreach (var entity in _spatialDirty)
		{
			_spatialIndex.Sync(entity);
		}

		_spatialDirty.Clear();
	}

	private void EnsureSpatialIndex()
	{
		if (_spatialIndex != null)
		{
			return;
		}

		_spatialIndex = new SpatialIndex();
		for (int i = 0; i < _entities.Count; i++)
		{
			if (_entities[i] is Entity concrete)
			{
				_spatialIndex.Sync(concrete);
			}
		}
	}

	private void HookActiveTracking(Entity entity)
	{
		entity.ActiveComponentsStateChanged += OnActiveComponentsStateChanged;
	}

	private void UnhookActiveTracking(Entity entity)
	{
		entity.ActiveComponentsStateChanged -= OnActiveComponentsStateChanged;
	}

	private void OnActiveComponentsStateChanged(Entity entity, bool isActive)
	{
		if (_isProcessingTick)
		{
			_pendingEntityListMutations.Enqueue(() => ApplyActiveChange(entity, isActive));
		}
		else
		{
			ApplyActiveChange(entity, isActive);
		}
	}

	private void ApplyActiveChange(Entity entity, bool isActive)
	{
		if (isActive)
		{
			if (!_activeEntities.Contains(entity)) _activeEntities.Add(entity);
		}
		else
		{
			_activeEntities.Remove(entity);
		}
	}

	private void FlushPendingEntityListMutations()
	{
		while (_pendingEntityListMutations.Count > 0)
		{
			var action = _pendingEntityListMutations.Dequeue();
			try { action?.Invoke(); }
			catch (Exception ex) { GD.PushError($"EntityManager: Active list mutation failed: {ex}"); }
		}
	}

	private readonly Dictionary<Entity, TransformComponent2D> _spatialTrackedTransforms = new();

	private void HookSpatialTracking(Entity entity)
	{
		if (entity == null || _spatialTracked.Contains(entity))
		{
			return;
		}

		_spatialTracked.Add(entity);
		var transform = entity.Transform;
		if (transform != null)
		{
			_spatialTrackedTransforms[entity] = transform;
			transform.PositionChanged += OnEntityMoved;
		}
	}

	private void UnhookSpatialTracking(Entity entity)
	{
		if (entity == null || !_spatialTracked.Remove(entity))
		{
			return;
		}

		// Unsubscribe from the tracked transform, not the current one
		if (_spatialTrackedTransforms.TryGetValue(entity, out var transform))
		{
			transform.PositionChanged -= OnEntityMoved;
			_spatialTrackedTransforms.Remove(entity);
		}
	}

	private void OnEntityMoved(Entity entity)
	{
		MarkSpatialDirty(entity);
	}
}
