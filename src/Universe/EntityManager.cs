using Godot;
using System;
using System.Collections.Generic;
using Game;
using Game.Data;
using Game.Data.Components;
using TG = Game.Data.Tags;
using Game.Utils;

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
	[Export]
	public bool BroadcastDespawnEvents { get; set; } = true;
	/// <summary>
	/// Maximum entities to prevent memory issues.
	/// </summary>
	private const int MAX_ENTITIES = 500000;

	public bool IsFull => _registry.Count >= MAX_ENTITIES;

	/// <summary>
	/// Delegated responsibilities for better separation of concerns
	/// </summary>
	private readonly EntityRegistry _registry = new(MAX_ENTITIES);
	private bool _isProcessingTick;

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

	public SpatialIndex SpatialPartition => _registry.SpatialPartition;

	public IReadOnlyList<IUpdatableEntity> AllEntities => _registry.Entities;
	public IReadOnlyList<IUpdatableEntity> GetEntities() => _registry.Entities;
	public bool RegisterEntity(IUpdatableEntity entity) => _registry.Register(entity);
	public bool UnregisterEntity(IUpdatableEntity entity, bool broadcastWorldEvent = true)
		=> _registry.Unregister(entity, broadcastWorldEvent && BroadcastDespawnEvents);

	public override void _Ready()
	{
		GD.Print("EntityManager: Ready");
		base._Ready();

		// Initialize target tag lookup for O(1) proximity queries
		TG.InitializeTargetTagLookup();

		// Wire up subsystems via events - EntityManager is just the coordinator
		// Note: EntityRegistry now handles all subsystem coordination internally
		
		GameManager.Instance.SubscribeToTick(OnTick);

		// Set global view defaults if provided
		if (ViewRoot != null)
		{
			ViewContext.DefaultParent = ViewRoot;
		}

		if (DefaultViewScene != null)
		{
			ViewContext.DefaultViewScene = DefaultViewScene;
		}

		// Ensure a CustomEntityRenderEngine exists for immediate-mode rendering
		if (EntityRendererFinder.Renderer == null)
		{
			var renderingEngine = new EntityRenderEngine();
			renderingEngine.EnableSpatialCulling = true;
			renderingEngine.CullingPadding = 256f;
			if (ViewRoot is Node2D vr2b)
				vr2b.AddChild(renderingEngine);
			else
				AddChild(renderingEngine);
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
			LM.Warning($"EntityManager: Skipping tick - still processing previous tick ({_currentTickWork.StartIndex}/{_currentTickWork.TotalEntities} entities updated)");
			return;
		}

		// Determine which list to process
		var entitiesToUpdate = _registry.ActiveCount > 0 ? _registry.ActiveEntities : _registry.Entities;

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
			return;
		

		_isProcessingTick = true;
		try
		{
			var entitiesToUpdate = _registry.ActiveCount > 0 ? _registry.ActiveEntities : _registry.Entities;

			// Calculate how many entities to process this frame
			int remainingEntities = _currentTickWork.TotalEntities - _currentTickWork.StartIndex;
			int entitiesToProcess = Math.Min(MaxEntitiesPerFrame, remainingEntities);
			int endIndex = _currentTickWork.StartIndex + entitiesToProcess;

			// Process the slice
			for (int i = _currentTickWork.StartIndex; i < endIndex; i++)
			{
				if (i < entitiesToUpdate.Count) // Safety check
					entitiesToUpdate[i].Update(_currentTickWork.Delta);
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
			_registry.FlushSpatialUpdates();
			_registry.ProcessPendingActiveMutations();
			_isProcessingTick = false;
		}
	}

	/// <summary>
	/// Gets an entity by its unique ID for efficient lookup.
	/// </summary>
	public Entity GetEntityById(Guid id) => _registry.GetById(id);

	public IReadOnlyList<Entity> QueryByTag(Tag tag, Vector2 position, float radius, int maxResults = int.MaxValue)
	{
		return _registry.QueryByTag(tag, position, radius, maxResults);
	}

	public IReadOnlyList<Entity> QueryByComponent<T>(Vector2 position, float radius, int maxResults = int.MaxValue) where T : class, IComponent
	{
		return _registry.QueryByComponent<T>(position, radius, maxResults);
	}

	public int EntityCount => _registry.Count;
	// No ViewCount here.

	public int ActiveEntityCount => _registry.ActiveCount;
}
