using System;
using System.Collections.Generic;
using Game.Data.Components;
using Godot;

namespace Game.Data.GOAP.GenericActions;

/// <summary>
/// Defines how to find target entities for an action
/// </summary>
public class EntityFinderConfig
{
    public Func<Entity, bool> Filter { get; set; }
    public float SearchRadius { get; set; } = float.MaxValue;
    public bool RequireReservation { get; set; } = false; // Must be reserved by us
    public bool RequireUnreserved { get; set; } = false; // Must be unreserved
    public bool ShouldReserve { get; set; } = false; // Should we reserve on Enter?
    
    /// <summary>
    /// Create finder for entities with a specific component type and optional additional filters
    /// </summary>
    public static EntityFinderConfig ByComponent<TComponent>(
        Func<TComponent, bool> componentFilter = null, 
        float radius = float.MaxValue,
        bool requireUnreserved = false,
        bool shouldReserve = false
    ) where TComponent : class, IComponent
    {
        return new EntityFinderConfig
        {
            Filter = e => 
            {
                if (!e.TryGetComponent<TComponent>(out var comp)) return false;
                return componentFilter?.Invoke(comp) ?? true;
            },
            SearchRadius = radius,
            RequireUnreserved = requireUnreserved,
            ShouldReserve = shouldReserve
        };
    }

    /// <summary>
    /// Create finder for entities with a specific TargetType
    /// </summary>
    public static EntityFinderConfig ByTargetType(
        TargetType targetType,
        float radius = float.MaxValue,
        bool requireUnreserved = false,
        bool shouldReserve = false
    )
    {
        return new EntityFinderConfig
        {
            Filter = e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == targetType,
            SearchRadius = radius,
            RequireUnreserved = requireUnreserved,
            ShouldReserve = shouldReserve
        };
    }
}

/// <summary>
/// Defines what happens when an interaction completes
/// </summary>
public class InteractionEffectConfig
{
    public Action<Entity, Entity> OnComplete { get; set; }
    public bool DestroyTargetOnComplete { get; set; } = false;
    public bool ReleaseReservationOnComplete { get; set; } = true;

    /// <summary>
    /// Pick up item into inventory
    /// </summary>
    public static InteractionEffectConfig PickUp(TargetType resourceType)
    {
        return new InteractionEffectConfig
        {
            OnComplete = (agent, target) =>
            {
                if (agent.TryGetComponent<NPCData>(out var npcData))
                {
                    npcData.Resources[resourceType] = (npcData.Resources.TryGetValue(resourceType, out var count) ? count : 0) + 1;
                    GD.Print($"Picked up 1 {resourceType}; total: {npcData.Resources[resourceType]}");
                }
            },
            DestroyTargetOnComplete = true,
            ReleaseReservationOnComplete = true
        };
    }

    /// <summary>
    /// Kill target entity (triggers loot drops etc)
    /// </summary>
    public static InteractionEffectConfig Kill()
    {
        return new InteractionEffectConfig
        {
            OnComplete = (agent, target) =>
            {
                if (target.TryGetComponent<HealthComponent>(out var health))
                {
                    health.Kill();
                    GD.Print($"Killed {target.Name}");
                }
            },
            DestroyTargetOnComplete = false, // HealthComponent handles destruction
            ReleaseReservationOnComplete = true
        };
    }

    /// <summary>
    /// Consume food to reduce hunger
    /// </summary>
    public static InteractionEffectConfig ConsumeFood()
    {
        return new InteractionEffectConfig
        {
            OnComplete = (agent, target) =>
            {
                if (target.TryGetComponent<FoodData>(out var foodData) &&
                    agent.TryGetComponent<NPCData>(out var npcData))
                {
                    float hungerRestored = foodData.HungerRestoredOnConsumption;
                    npcData.Hunger = Mathf.Max(0, npcData.Hunger - hungerRestored);
                    GD.Print($"Consumed {target.Name}! Hunger: {npcData.Hunger}/{npcData.MaxHunger}");
                }
            },
            DestroyTargetOnComplete = true,
            ReleaseReservationOnComplete = true
        };
    }

    public static InteractionEffectConfig Arbitrary(Action<Entity, Entity> onComplete)
    {
        return new InteractionEffectConfig
        {
            OnComplete = onComplete,
            DestroyTargetOnComplete = false,
            ReleaseReservationOnComplete = true
        };
    }
}
