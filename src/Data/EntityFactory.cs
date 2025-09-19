using Godot;
using System;
using Game.Data.Components;

namespace Game.Data;

/// <summary>
/// Centralized factory for constructing fully-initialized entities from blueprints.
/// Applies tags, components, blueprint mutators, and completes initialization.
/// Does not register entities with managers or scene systems.
/// </summary>
public static class EntityFactory
{
    public static Entity Create(EntityBlueprint blueprint, Vector2? position = null)
    {
        if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));

        // Build base entity and add all components (pre-attach already runs in AddComponent)
        var entity = Entity.From(blueprint);

        // Apply initial transform if provided
        if (position.HasValue)
        {
            var t = entity.GetComponent<TransformComponent2D>();
            if (t != null) t.Position = position.Value;
        }

        // Run blueprint mutators (root -> leaf) prior to post-attach
        foreach (var mutate in blueprint.GetAllMutators())
        {
            try
            {
                mutate?.Invoke(entity);
            }
            catch (Exception ex)
            {
                GD.PushWarning($"EntityFactory: Mutator threw in blueprint '{blueprint.Name}': {ex.Message}");
            }
        }

        // Finalize attachment (post-attach + active registration)
        entity.Initialize();
        return entity;
    }
}


