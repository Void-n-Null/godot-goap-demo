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
    /// Returns the chain of blueprints from root base to this one (inclusive).
    /// </summary>
    public IEnumerable<EntityBlueprint> EnumerateRootToLeaf()
    {
        var stack = new Stack<EntityBlueprint>();
        for (var bp = this; bp != null; bp = bp.Base)
        {
            stack.Push(bp);
        }
        while (stack.Count > 0) yield return stack.Pop();
    }

    public IEnumerable<Tag> GetAllTags()
    {
        // Union while preserving order of appearance across the chain
        var seen = new HashSet<Tag>();
        foreach (var bp in EnumerateRootToLeaf())
        {
            foreach (var tag in bp.Tags)
            {
                if (seen.Add(tag)) yield return tag;
            }
        }
    }

    public IEnumerable<IComponent> CreateAllComponents()
    {
        foreach (var bp in EnumerateRootToLeaf())
        {
            var comps = bp.ComponentsFactory?.Invoke();
            if (comps == null) continue;
            foreach (var c in comps) yield return c;
        }
    }

    public string ResolveScenePath()
    {
        string path = null;
        foreach (var bp in EnumerateRootToLeaf())
        {
            if (!string.IsNullOrEmpty(bp.ScenePath)) path = bp.ScenePath;
        }
        return path;
    }

    /// <summary>
    /// Creates a derived blueprint by layering additions/overrides on top of this blueprint.
    /// </summary>
    public EntityBlueprint Derive(
        string name,
        string scenePath = null,
        IEnumerable<Tag> addTags = null,
        Func<IEnumerable<IComponent>> addComponents = null)
    {
        var tags = addTags is null ? Array.Empty<Tag>() : addTags.ToArray();
        Func<IEnumerable<IComponent>> factory = addComponents ?? (() => Array.Empty<IComponent>());
        return new EntityBlueprint(
            name: name,
            scenePath: scenePath,
            tags: tags,
            componentsFactory: factory,
            @base: this
        );
    }
}


