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
    private sealed class FactoryEntity : Entity { }

    public static Entity Create(EntityBlueprint blueprint, params Action<Entity>[] additionalMutators)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var entity = new FactoryEntity();

        // Add tags
        foreach (var tag in blueprint.GetAllTags())
            entity.AddTag(tag);

        // Components (respect duplicate policy)
        var seenTypes = new System.Collections.Generic.HashSet<Type>();
        var warnedTypes = new System.Collections.Generic.HashSet<Type>();
        foreach (var comp in blueprint.CreateAllComponents())
        {
            var t = comp.GetType();
            var policy = blueprint.GetDuplicatePolicy(t);
            if (seenTypes.Contains(t))
            {
                switch (policy)
                {
                    case DuplicatePolicy.Prohibit:
                        if (!warnedTypes.Contains(t))
                        {
                            GD.PushWarning($"EntityFactory: Duplicate component {t.Name} in blueprint '{blueprint.Name}' prohibited; skipping.");
                            warnedTypes.Add(t);
                        }
                        continue;
                    case DuplicatePolicy.AllowMultiple:
                        if (!warnedTypes.Contains(t))
                        {
                            GD.PushWarning($"EntityFactory: Duplicate component {t.Name} allowed by policy but storage supports only one per type; replacing previous instance.");
                            warnedTypes.Add(t);
                        }
                        break; // replace previous instance (type-keyed storage)
                    case DuplicatePolicy.Replace:
                    default:
                        break;
                }
            }

            entity.AddComponent(comp);
            seenTypes.Add(t);
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

        // Run caller-provided additional mutators (override-friendly)
        if (additionalMutators != null)
        {
            foreach (var mutate in additionalMutators)
            {
                try
                {
                    mutate?.Invoke(entity);
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"EntityFactory: Additional mutator threw: {ex.Message}");
                }
            }
        }

        // Finalize attachment (post-attach + active registration)
        entity.Initialize();
        return entity;
    }
}


