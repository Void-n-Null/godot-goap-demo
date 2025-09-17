using Godot;
using System;
using System.Collections.Generic;
using Game;
using Game.Data;
using Game.Data.Components;

namespace Game.Universe;

/// <summary>
/// Flexible entity manager that supports entities with and without views.
/// Uses efficient array-based storage instead of dictionaries for performance.
/// </summary>
public partial class EntityManager : Utils.SingletonNode2D<EntityManager>
{
    /// <summary>
    /// Maximum entities to prevent memory issues.
    /// </summary>
    private const int MAX_ENTITIES = 10000;

    /// <summary>
    /// All entities (with or without views) for bulk updates.
    /// Fast array iteration, no dictionary overhead.
    /// </summary>
    private readonly List<IUpdatableEntity> _entities = new(MAX_ENTITIES);

    /// <summary>
    /// Optional view mapping - only for entities that have visual representation.
    /// Small dictionary is fine since not all entities need views.
    /// </summary>
    private readonly Dictionary<IUpdatableEntity, Node2D> _entityToView = new();

    /// <summary>
    /// Reverse lookup for view-to-entity mapping.
    /// </summary>
    private readonly Dictionary<Node2D, IUpdatableEntity> _viewToEntity = new();

    public override void _Ready()
    {
        GD.Print("EntityManager: Ready");
        base._Ready();

        // Subscribe to GameManager's efficient tick system
        GameManager.Instance.SubscribeToTick(OnTick);
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
    public bool RegisterEntityWithView(IUpdatableEntity entity, Node2D view)
    {
        if (!RegisterEntity(entity))
            return false;

        _entityToView[entity] = view;
        _viewToEntity[view] = entity;

        // Add view to scene if not already added
        if (view.GetParent() == null)
        {
            AddChild(view);
        }

        return true;
    }

    /// <summary>
    /// Unregisters an entity from updates.
    /// </summary>
    public bool UnregisterEntity(IUpdatableEntity entity)
    {
        if (_entities.Remove(entity))
        {
            // Remove view mapping if it exists
            if (_entityToView.TryGetValue(entity, out var view))
            {
                _entityToView.Remove(entity);
                _viewToEntity.Remove(view);

                // Optionally remove view from scene
                if (view.GetParent() == this)
                {
                    RemoveChild(view);
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the view for an entity (if it has one).
    /// </summary>
    public Node2D GetViewForEntity(IUpdatableEntity entity)
    {
        return _entityToView.GetValueOrDefault(entity);
    }

    /// <summary>
    /// Gets the entity for a view (if it exists).
    /// </summary>
    public IUpdatableEntity GetEntityForView(Node2D view)
    {
        return _viewToEntity.GetValueOrDefault(view);
    }

    /// <summary>
    /// Gets all entities (for iteration if needed).
    /// </summary>
    public IReadOnlyList<IUpdatableEntity> GetEntities() => _entities;

    public int EntityCount => _entities.Count;
    public int ViewCount => _entityToView.Count;
}


// ===== USAGE EXAMPLES =====

/// <summary>
/// Simple data entity (inherits from base Entity).
/// </summary>
public class GameStateEntity : Entity
{
    public int Score { get; set; }
    public float GameTime { get; set; }

    public override void Update(double delta)
    {
        base.Update(delta); // Updates components

        if (IsActive)
        {
            GameTime += (float)delta;

            if (GameTime > 60 && Score < 100)
            {
                GD.Print("Game Over - Time's up!");
            }
        }
    }
}

/// <summary>
/// Player entity with movement and visuals.
/// </summary>
public class PlayerEntity : VisualEntity2D
{
    public MovementComponent Movement => GetComponent<MovementComponent>();
    public HealthComponent Health => GetComponent<HealthComponent>();

    public PlayerEntity(Vector2 position) : base(position, "res://scenes/PlayerView.tscn")
    {
        // Add specialized components (Phase 1: PreAttached called automatically)
        AddComponent(new MovementComponent { MaxSpeed = 300f });
        AddComponent(new HealthComponent(150f));

        // Complete attachment (Phase 2: PostAttached called for all components)
        Initialize();

        // Now safe to setup component interactions (all components are fully attached)
        Health.OnDeath += () => GD.Print("Player died!");
        Health.OnHealthChanged += (health) => GD.Print($"Player health: {health}");

        // Add tags for filtering
        Tags.Add("Player");
        Tags.Add("Controllable");
    }

    public override void Update(double delta)
    {
        base.Update(delta); // Updates all components including visuals

        // Player-specific logic
        HandleInput();
    }

    private void HandleInput()
    {
        var input = Input.GetVector("left", "right", "up", "down");
        Movement.Acceleration = input * 1000f; // Strong acceleration from input
    }
}

/// <summary>
/// AI enemy entity.
/// </summary>
public class EnemyEntity : VisualEntity2D
{
    public MovementComponent Movement => GetComponent<MovementComponent>();
    public HealthComponent Health => GetComponent<HealthComponent>();

    public EnemyEntity(Vector2 position) : base(position, "res://scenes/EnemyView.tscn")
    {
        // Phase 1: Add components (PreAttached called)
        AddComponent(new MovementComponent { MaxSpeed = 150f });
        AddComponent(new HealthComponent(50f));

        // Phase 2: Complete attachment (PostAttached called for all)
        CompleteAttachment();

        // Now safe to setup interactions
        Tags.Add("Enemy");
        Tags.Add("AI");

        Health.OnDeath += () => Destroy();
    }

    public override void Update(double delta)
    {
        base.Update(delta);

        // Simple AI: move toward player
        var player = FindNearestPlayer();
        if (player != null)
        {
            var direction = (player.Position.Position - Position.Position).Normalized();
            Movement.Acceleration = direction * 200f;
        }
    }

    private PlayerEntity FindNearestPlayer()
    {
        // This would need implementation based on your spatial partitioning
        // For now, return null (no AI movement)
        return null;
    }
}