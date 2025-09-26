using Godot;
using System;
using System.Collections.Generic;
using Game.Data;
using Game.Universe;
using Game.Utils;
using Random = Game.Utils.Random;

namespace Game.Data.Components;

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

    public List<EntityBlueprint> EntitiesToSpawnOnDeath { get; set; } = [];
    public Action OnDeathAction { get; set; }

    public Entity Entity { get; set; }

    public HealthComponent(float maxHealth = 100f, List<EntityBlueprint> entitiesToSpawnOnDeath = null, Action onDeathAction = null)
    {
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
        EntitiesToSpawnOnDeath = entitiesToSpawnOnDeath;
        OnDeathAction = onDeathAction;
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive) return;

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        OnHealthChanged?.Invoke(CurrentHealth);

        if (CurrentHealth <= 0)
        {
            OnDeath();
        }
    }

    public void Heal(float amount)
    {
        if (!IsAlive) return;

        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        OnHealthChanged?.Invoke(CurrentHealth);
    }

    public void Kill()
    {
        if (!IsAlive) return;

        CurrentHealth = 0;
        OnHealthChanged?.Invoke(CurrentHealth);
        OnDeath();
    }

    // No per-frame Update: metadata-only; reacts via events

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

    public void OnDeath()
    {
        foreach (var blueprint in EntitiesToSpawnOnDeath)
        {
            SpawnEntity.Now(blueprint, Entity.Transform.Position + Random.InsideCircle(Vector2.Zero, 100));
        }
        EntityManager.Instance.UnregisterEntity(Entity);
        Entity.Destroy();

        OnDeathAction?.Invoke();
    }

    public void OnDetached()
    {
        // Clean up event handlers
        OnHealthChanged = null;
        OnDeathAction = null;
    }
}
