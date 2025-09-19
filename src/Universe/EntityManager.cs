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
	/// <summary>
	/// Maximum entities to prevent memory issues.
	/// </summary>
	private const int MAX_ENTITIES = 10000;

	/// <summary>
	/// All entities (with or without views) for bulk updates.
	/// Fast array iteration, no dictionary overhead.
	/// </summary>
	private readonly List<IUpdatableEntity> _entities = new(MAX_ENTITIES);

	// Intentionally no view mappings. Views are owned by VisualComponent.

	public override void _Ready()
	{
		GD.Print("EntityManager: Ready");
		base._Ready();

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
	}

	/// <summary>
	/// Ultra-fast bulk update - direct array iteration.
	/// No dictionary lookups, no event delegation overhead.
	/// </summary>
	private void OnTick(double delta)
	{
		for (int i = 0; i < _entities.Count; i++)
		{
			_entities[i].Update(delta);
		}
	}

	/// <summary>
	/// Registers any entity for bulk updates.
	/// Works for both view-based and view-less entities.
	/// </summary>
	public bool RegisterEntity(IUpdatableEntity entity)
	{
		if (_entities.Count >= MAX_ENTITIES)
		{
			GD.PushWarning($"EntityManager: Max entities ({MAX_ENTITIES}) reached");
			return false;
		}

		if (!_entities.Contains(entity))
		{
			_entities.Add(entity);
			return true;
		}

		return false;
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
		if (_entities.Remove(entity))
		{
			return true;
		}
		return false;
	}

	/// <summary>
	/// Gets all entities (for iteration if needed).
	/// </summary>
	public IReadOnlyList<IUpdatableEntity> GetEntities() => _entities;

	public int EntityCount => _entities.Count;
	// No ViewCount here.

	/// <summary>
	/// Spawns an entity from a blueprint, optionally sets position, registers and initializes it.
	/// </summary>
	public Entity Spawn(EntityBlueprint blueprint, Vector2? position = null)
	{
		var entity = EntityFactory.Create(blueprint, position);

		// Register (views attach themselves inside VisualComponent)
		RegisterEntity(entity);
		return entity;
	}
}
