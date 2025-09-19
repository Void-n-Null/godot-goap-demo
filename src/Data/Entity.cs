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
    public HashSet<Tag> Tags { get; } = [];

    /// <summary>
    /// Adds a tag to this entity.
    /// </summary>
    public bool AddTag(Tag tag) => Tags.Add(tag);

    /// <summary>
    /// Adds a tag from its string name (bridged via registry).
    /// </summary>
    public bool AddTag(string tagName) => Tags.Add(Tag.From(tagName));

    /// <summary>
    /// Removes a tag from this entity.
    /// </summary>
    public bool RemoveTag(Tag tag) => Tags.Remove(tag);

    /// <summary>
    /// Removes a tag by its string name.
    /// </summary>
    public bool RemoveTag(string tagName)
    {
        return Tag.TryFrom(tagName, out var tag) && Tags.Remove(tag);
    }

    /// <summary>
    /// Checks if this entity has the given tag.
    /// </summary>
    public bool HasTag(Tag tag) => Tags.Contains(tag);

    /// <summary>
    /// Checks if this entity has the tag by name.
    /// </summary>
    public bool HasTag(string tagName)
    {
        return Tag.TryFrom(tagName, out var tag) && Tags.Contains(tag);
    }

    /// <summary>
    /// Whether this entity should be updated.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Components attached to this entity.
    /// </summary>
    protected readonly Dictionary<Type, IComponent> _components = [];

    /// <summary>
    /// Active components that should receive per-frame updates.
    /// Stored in a list for tight iteration.
    /// </summary>
    protected readonly List<IActiveComponent> _activeComponents = [];

    /// <summary>
    /// Tracks whether this entity has completed initial attachment.
    /// Controls whether newly added components should run OnPostAttached immediately.
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// Gets a component of the specified type, or null if not found.
    /// </summary>
    public T GetComponent<T>() where T : class, IComponent
    {
        if (_components.TryGetValue(typeof(T), out var component))
        {
            return (T)component;
        }

        // Polymorphic fallback: search for first component that implements T
        foreach (var c in _components.Values)
        {
            if (c is T match) return match;
        }

        return null;
    }

    /// <summary>
    /// Adds or replaces a component using two-phase attachment.
    /// Uses the component's runtime type for storage to avoid interface-type overwrites.
    /// </summary>
    public void AddComponent<T>(T component) where T : IComponent
    {
        var key = component.GetType();
        if (_components.TryGetValue(key, out var existing))
        {
            if (existing is IActiveComponent existingActive)
            {
                _activeComponents.Remove(existingActive);
            }
            existing.OnDetached();
            existing.Entity = null;
        }

        _components[key] = component;
        component.Entity = this;

        // Two-phase attachment for safe component initialization
        component.OnPreAttached();

        // If the entity is already initialized, complete attachment immediately
        if (_isInitialized)
        {
            component.OnPostAttached();
            if (component is IActiveComponent active)
            {
                _activeComponents.Add(active);
            }
        }
    }

    /// <summary>
    /// Adds or replaces a component when only the interface type is available at callsite.
    /// </summary>
    public void AddComponent(IComponent component)
    {
        var key = component.GetType();
        if (_components.TryGetValue(key, out var existing))
        {
            if (existing is IActiveComponent existingActive)
            {
                _activeComponents.Remove(existingActive);
            }
            existing.OnDetached();
            existing.Entity = null;
        }

        _components[key] = component;
        component.Entity = this;
        component.OnPreAttached();

        if (_isInitialized)
        {
            component.OnPostAttached();
            if (component is IActiveComponent active)
            {
                _activeComponents.Add(active);
            }
        }
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
            if (component is IActiveComponent active)
            {
                _activeComponents.Add(active);
            }
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
            if (component is IActiveComponent active)
            {
                _activeComponents.Remove(active);
            }
            return _components.Remove(typeof(T));
        }
        return false;
    }

    /// <summary>
    /// Checks if entity has a specific component.
    /// </summary>
    public bool HasComponent<T>() where T : IComponent
    {
        if (_components.ContainsKey(typeof(T))) return true;
        foreach (var c in _components.Values)
        {
            if (c is T) return true;
        }
        return false;
    }


    /// <summary>
    /// Initializes the entity by completing the attachment of the components it was created with.
    /// </summary>
    public void Initialize()
    {
        _isInitialized = true;
        CompleteAttachment();
    }

    /// <summary>
    /// Core update method - calls Update on all components.
    /// </summary>
    public virtual void Update(double delta)
    {
        if (!IsActive) return;

        // Fast path: only iterate active components
        for (int i = 0; i < _activeComponents.Count; i++)
        {
            _activeComponents[i].Update(delta);
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
        _activeComponents.Clear();
    }

    /// <summary>
    /// Factory: builds an Entity from an EntityBlueprint. If a non-empty ScenePath is resolved
    /// and no VisualComponent is added by the blueprint, a VisualComponent is auto-added.
    /// Does not set position or initialize; caller should set any initial state and then call Initialize().
    /// </summary>
    public static Entity From(EntityBlueprint blueprint)
    {
        // Plain concrete entity type (no inheritance tree required)
        var entity = new PlainEntity();

        // Base transform for 2D entities is just a TransformComponent2D in the blueprint chain.
        foreach (var tag in blueprint.GetAllTags())
        {
            entity.AddTag(tag);
        }

        // Materialize all components
        bool hasVisual = false;
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
                            GD.PushWarning($"Entity.From: Duplicate component {t.Name} in blueprint '{blueprint.Name}' prohibited; skipping.");
                            warnedTypes.Add(t);
                        }
                        continue;
                    case DuplicatePolicy.AllowMultiple:
                        if (!warnedTypes.Contains(t))
                        {
                            GD.PushWarning($"Entity.From: Duplicate component {t.Name} allowed by policy but storage supports only one per type; replacing previous instance.");
                            warnedTypes.Add(t);
                        }
                        break; // fall through to replacement behavior
                    case DuplicatePolicy.Replace:
                    default:
                        break;
                }
            }

            entity.AddComponent(comp);
            seenTypes.Add(t);
            if (comp is VisualComponent) hasVisual = true;
        }

        // If a ScenePath is resolved but no VisualComponent provided, add one
        var scenePath = blueprint.ResolveScenePath();
        if (!string.IsNullOrEmpty(scenePath) && !hasVisual)
        {
            entity.AddComponent(new VisualComponent(scenePath));
        }

        // Caller is responsible for setting transform data and calling Initialize()
        return entity;
    }

    /// <summary>
    /// Minimal concrete entity since Entity is abstract.
    /// </summary>
    private sealed class PlainEntity : Entity { }
}









