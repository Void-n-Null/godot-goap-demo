using Godot;
using Game.Data;
using Game.Data.Components;
using Game.Universe;
using System.Collections.Generic;
using Random = Game.Utils.Random;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Component that spawns items when the entity dies.
/// Listens to HealthComponent death events.
/// </summary>
public class LootDropComponent : IComponent
{
    public Entity Entity { get; set; }
    
    /// <summary>
    /// Items to drop on death with their quantities
    /// </summary>
    public List<LootDrop> Drops { get; set; } = new();
    
    /// <summary>
    /// Radius in which to scatter dropped items around the death position
    /// </summary>
    public float ScatterRadius { get; set; } = 50f;
    
    private HealthComponent _healthComponent;

    public void OnPreAttached()
    {
        // Basic initialization
    }

    public void OnPostAttached()
    {
        // Subscribe to health component death event
        _healthComponent = Entity.GetComponent<HealthComponent>();
        if (_healthComponent != null)
        {
            _healthComponent.OnDeathAction += OnEntityDeath;
        }
        else
        {
            LM.Warning($"LootDropComponent on {Entity.Name} but no HealthComponent found - loot will never drop!");
        }
    }

    private void OnEntityDeath()
    {
        if (Drops.Count == 0) return;
        
        var spawnPos = Entity.Transform.Position;
        LM.Debug($"LootDropComponent: {Entity.Name} died at {spawnPos}. Dropping loot:");
        
        foreach (var drop in Drops)
        {
            for (int i = 0; i < drop.Quantity; i++)
            {
                var offset = Random.InsideCircle(Vector2.Zero, ScatterRadius);
                var finalPos = spawnPos + offset;
                var spawned = SpawnEntity.Now(drop.Blueprint, finalPos);
                
                if (i == 0) // Log once per drop type
                {
                    LM.Debug($"  - Dropping {drop.Quantity}x {drop.Blueprint.Name} (scatter radius: {ScatterRadius})");
                }
            }
        }
    }

    public void OnDetached()
    {
        if (_healthComponent != null)
        {
            _healthComponent.OnDeathAction -= OnEntityDeath;
        }
    }
}

/// <summary>
/// Represents a single loot drop: what to spawn and how many
/// </summary>
public class LootDrop
{
    public EntityBlueprint Blueprint { get; set; }
    public int Quantity { get; set; } = 1;

    public LootDrop() { }
    
    public LootDrop(EntityBlueprint blueprint, int quantity = 1)
    {
        Blueprint = blueprint;
        Quantity = quantity;
    }
}
