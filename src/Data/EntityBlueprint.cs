using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data.Components;

namespace Game.Data;

/// <summary>
/// Data-driven blueprint describing how to build an Entity.
/// Supports base-chaining for compositional inheritance.
/// ComponentsFactory must return NEW component instances per call.
/// </summary>
public sealed class EntityBlueprint
{
    public EntityBlueprint Base { get; init; }
    public string Name { get; init; }
    public string ScenePath { get; init; } // null for headless
    public IReadOnlyCollection<Tag> Tags { get; init; } = Array.Empty<Tag>();
    public Func<IEnumerable<IComponent>> ComponentsFactory { get; init; } = () => Array.Empty<IComponent>();
    public IReadOnlyCollection<Action<Entity>> Mutators { get; init; } = Array.Empty<Action<Entity>>();
    public IReadOnlyDictionary<Type, DuplicatePolicy> DuplicatePolicies { get; init; } = new Dictionary<Type, DuplicatePolicy>();

    public EntityBlueprint(
        string name,
        string scenePath = null,
        IReadOnlyCollection<Tag> tags = null,
        Func<IEnumerable<IComponent>> componentsFactory = null,
        EntityBlueprint @base = null)
    {
        Name = name;
        ScenePath = scenePath;
        Tags = tags ?? Array.Empty<Tag>();
        ComponentsFactory = componentsFactory ?? (() => Array.Empty<IComponent>());
        Base = @base;
    }


    /// <summary>
    /// Creates a derived blueprint by layering additions/overrides on top of this blueprint.
    /// </summary>
    public EntityBlueprint Derive(
        string name,
        string scenePath = null,
        IEnumerable<Tag> addTags = null,
        Func<IEnumerable<IComponent>> addComponents = null,
        IEnumerable<Action<Entity>> addMutators = null,
        IReadOnlyDictionary<Type, DuplicatePolicy> duplicatePolicies = null)
    {
        var tags = addTags is null ? Array.Empty<Tag>() : addTags.ToArray();
        Func<IEnumerable<IComponent>> factory = addComponents ?? (() => Array.Empty<IComponent>());
        var mutators = addMutators is null ? Array.Empty<Action<Entity>>() : addMutators.ToArray();
        var policies = duplicatePolicies ?? new Dictionary<Type, DuplicatePolicy>();
        return new EntityBlueprint(
            name: name,
            scenePath: scenePath,
            tags: tags,
            componentsFactory: factory,
            @base: this
        )
        {
            Mutators = mutators,
            DuplicatePolicies = policies
        };
    }

    public static Action<Entity> Mutate<T>(Action<T> mutate) where T : class, IComponent
    {
        if (mutate == null) throw new ArgumentNullException(nameof(mutate));
        return entity =>
        {
            if (entity == null) return;
            var c = entity.GetComponent<T>();
            if (c != null) mutate(c);
        };
    }

    public static Action<Entity> Mutate<T>(Action<T, Entity> mutate) where T : class, IComponent
    {
        if (mutate == null) throw new ArgumentNullException(nameof(mutate));
        return entity =>
        {
            if (entity == null) return;
            var c = entity.GetComponent<T>();
            if (c != null) mutate(c, entity);
        };
    }


}

public enum DuplicatePolicy
{
    Replace,
    Prohibit,

}


