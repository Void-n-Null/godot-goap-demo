using Godot;
using System;
using Game.Data.Components;
using System.Collections.Generic;
#nullable enable
namespace Game.Data;

/// <summary>
/// Centralized factory for constructing fully-initialized entities from blueprints.
/// Applies tags, components, blueprint mutators, and completes initialization.
/// Does not register entities with managers or scene systems.
/// </summary>
public static class EntityFactory
{
    public sealed class EntityCreateOptions
    {
        public CreationErrorMode MutatorErrorType { get; init; } = CreationErrorMode.Warning;
        public CreationErrorMode RequiredComponentErrorType { get; init; } = CreationErrorMode.Strict;
        public Action<string>? LogWarning { get; init; } = GD.PushWarning;
    }

    public enum CreationErrorMode{
        Strict,
        Warning,
        Ignore
    }

    public static Entity Create(EntityBlueprint blueprint, Action<Entity>[]? additionalMutators = null, EntityCreateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        options ??= new EntityCreateOptions();
        
        var entity = new Entity
        {
            Name = blueprint.Name
        };

        // Add tags
        foreach (var tag in blueprint.GetAllTags())
            entity.AddTag(tag);

        // Components (respect duplicate policy)
        var seenTypes = new HashSet<Type>();
        var warnedTypes = new HashSet<Type>();
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
                            options.LogWarning?.Invoke($"EntityFactory: Duplicate component {t.Name} in blueprint '{blueprint.Name}' prohibited; skipping.");
                            warnedTypes.Add(t);
                        }
                        continue;
                    case DuplicatePolicy.Replace:
                    default:
                        break;
                }
            }

            entity.AddComponent(comp);
            seenTypes.Add(t);
        }

        var allMutators = new List<Action<Entity>>(blueprint.GetAllMutators());
        if (additionalMutators != null && additionalMutators.Length > 0)
            allMutators.AddRange(additionalMutators);
        

        // Run blueprint mutators (root -> leaf) prior to post-attach
        entity.ApplyMutators(allMutators, options);

        // Validate required component dependencies before initialization
        ValidateRequiredComponents(entity, blueprint, options);

        // Initialize the entity, which will allow components to reference each other and complete their attachment to the entity
        entity.Initialize();
        return entity;
    }

    private static void ApplyMutators(this Entity entity, IReadOnlyList<Action<Entity>> mutators, EntityCreateOptions options)
    {
        foreach (var mutate in mutators)
        {
            try
            {
                mutate?.Invoke(entity);
            }
            catch (Exception ex)
            {
                switch (options.MutatorErrorType)
                {
                    case CreationErrorMode.Strict:
                        throw;
                    case CreationErrorMode.Warning:
                        options.LogWarning?.Invoke($"EntityFactory: Mutator threw: {ex.Message}");
                        break;
                    case CreationErrorMode.Ignore:
                        break;
                }
            }
        }
    }

        /// <summary>
    /// Returns the chain of blueprints from root base to this one (inclusive).
    /// </summary>
    private static IEnumerable<EntityBlueprint> EnumerateRootToLeaf(this EntityBlueprint blueprint)
    {
        var stack = new Stack<EntityBlueprint>();
        for (var bp = blueprint; bp != null; bp = bp.Base)
            stack.Push(bp);
        while (stack.Count > 0) yield return stack.Pop();
    }

    private static IEnumerable<Tag> GetAllTags(this EntityBlueprint blueprint)
    {
        // Union while preserving order of appearance across the chain
        var seen = new HashSet<Tag>();
        foreach (var bp in blueprint.EnumerateRootToLeaf())
        {
            foreach (var tag in bp.Tags)
            {
                if (seen.Add(tag)) yield return tag;
            }
        }
    }

    private static IEnumerable<IComponent> CreateAllComponents(this EntityBlueprint blueprint)
    {
        foreach (var bp in blueprint.EnumerateRootToLeaf())
        {
            var comps = bp.ComponentsFactory?.Invoke();
            if (comps == null) continue;
            foreach (var c in comps) yield return c;
        }
    }
    private static IEnumerable<Action<Entity>> GetAllMutators(this EntityBlueprint blueprint)
    {
        foreach (var bp in blueprint.EnumerateRootToLeaf())
        {
            if (bp.Mutators == null) continue;
            foreach (var m in bp.Mutators) yield return m;
        }
    }

    private static DuplicatePolicy GetDuplicatePolicy(this EntityBlueprint blueprint, Type componentType)
    {
        DuplicatePolicy? policy = null;
        foreach (var bp in blueprint.EnumerateRootToLeaf())
        {
            if (bp.DuplicatePolicies != null && bp.DuplicatePolicies.TryGetValue(componentType, out var p))
            {
                policy = p;
            }
        }
        return policy ?? DuplicatePolicy.Replace;
    }

    private static void ValidateRequiredComponents(Entity entity, EntityBlueprint blueprint, EntityCreateOptions options)
    {
        foreach (var component in entity.GetAllComponents())
        {
            var required = component.GetRequiredComponents();
            if (required == null) continue;
            foreach (var dep in required)
            {
                var type = dep.ComponentType;
                if (type == null) continue;
                if (!entity.HasComponent(type))
                {
                    var message = $"EntityFactory: Component {component.GetType().Name} requires {type.Name} on blueprint '{blueprint.Name}'";
                    switch (options.RequiredComponentErrorType)
                    {
                        case CreationErrorMode.Strict:
                            throw new InvalidOperationException(message);
                        case CreationErrorMode.Warning:
                            options.LogWarning?.Invoke(message);
                            break;
                        case CreationErrorMode.Ignore:
                            break;
                    }
                }
            }
        }
    }
}


