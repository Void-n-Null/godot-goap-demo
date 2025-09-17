using Godot;
using System;
using System.Collections.Generic;
using Game.Data.Components;


namespace Game.Data;

/// <summary>
/// Base entity with core ECS functionality.
/// Supports component-based architecture with inheritable components.
/// </summary>
public abstract class Entity : IUpdatableEntity
{
    /// <summary>
    /// Unique entity ID for component lookup and debugging.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Entity tags for categorization and filtering.
    /// </summary>
    public HashSet<string> Tags { get; } = new();

    /// <summary>
    /// Whether this entity should be updated.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Components attached to this entity.
    /// </summary>
    protected readonly Dictionary<Type, IComponent> _components = new();

    /// <summary>
    /// Gets a component of the specified type.
    /// </summary>
    public T GetComponent<T>() where T : IComponent
    {
        return _components.TryGetValue(typeof(T), out var component) ? (T)component : default;
    }

    /// <summary>
    /// Adds or replaces a component using two-phase attachment.
    /// </summary>
    public void AddComponent<T>(T component) where T : IComponent
    {
        _components[typeof(T)] = component;
        component.Entity = this;

        // Two-phase attachment for safe component initialization
        component.OnPreAttached();
    }

    /// <summary>
    /// Completes component attachment after all components are added.
    /// Call this after adding all components to an entity.
    /// </summary>
    private void CompleteAttachment()
    {
        foreach (var component in _components.Values)
        {
            component.OnPostAttached();
        }
    }

    /// <summary>
    /// Removes a component.
    /// </summary>
    public bool RemoveComponent<T>() where T : IComponent
    {
        if (_components.TryGetValue(typeof(T), out var component))
        {
            component.OnDetached();
            component.Entity = null;
            return _components.Remove(typeof(T));
        }
        return false;
    }

    /// <summary>
    /// Checks if entity has a specific component.
    /// </summary>
    public bool HasComponent<T>() where T : IComponent => _components.ContainsKey(typeof(T));


    /// <summary>
    /// Initializes the entity by completing the attachment of the components it was created with.
    /// </summary>
    public void Initialize()
    {
        CompleteAttachment();
    }

    /// <summary>
    /// Core update method - calls Update on all components.
    /// </summary>
    public virtual void Update(double delta)
    {
        if (!IsActive) return;

        foreach (var component in _components.Values)
        {
            component.Update(delta);
        }
    }

    /// <summary>
    /// Called when entity is destroyed.
    /// </summary>
    public virtual void Destroy()
    {
        foreach (var component in _components.Values)
        {
            component.OnDetached();
        }
        _components.Clear();
    }
}









/// <summary>
/// Health component with damage and healing.
/// </summary>
public class HealthComponent : IComponent
{
    public float MaxHealth { get; set; } = 100f;
    public float CurrentHealth { get; set; }

    public bool IsAlive => CurrentHealth > 0;
    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0f;

    public event Action<float> OnHealthChanged;
    public event Action OnDeath;

    public Entity Entity { get; set; }

    public HealthComponent(float maxHealth = 100f)
    {
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive) return;

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        OnHealthChanged?.Invoke(CurrentHealth);

        if (CurrentHealth <= 0)
        {
            OnDeath?.Invoke();
        }
    }

    public void Heal(float amount)
    {
        if (!IsAlive) return;

        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        OnHealthChanged?.Invoke(CurrentHealth);
    }

    public void Update(double delta)
    {
        // Could implement health regeneration, poison effects, etc.
    }

    public void OnPreAttached()
    {
        // Phase 1: Basic health setup
        // Could validate health values, set up internal state
    }

    public void OnPostAttached()
    {
        // Phase 2: Could interact with other components
        // For example, could listen to movement events, or affect visual appearance
        var visual = Entity.GetComponent<VisualComponent>();
        if (visual != null)
        {
            // Could set up visual feedback for health changes
            OnHealthChanged += (health) => UpdateVisualHealthFeedback(visual, health);
        }
    }

    private void UpdateVisualHealthFeedback(VisualComponent visual, float health)
    {
        // Could modify visual appearance based on health
        if (visual.ViewNode != null)
        {
            // Example: Change color based on health
            float healthRatio = HealthPercentage;
            var color = new Color(1, healthRatio, healthRatio); // Red when low health
            // This would require the ViewNode to have a Sprite2D or similar
        }
    }

    public void OnDetached()
    {
        // Clean up event handlers
        OnHealthChanged = null;
        OnDeath = null;
    }
}
