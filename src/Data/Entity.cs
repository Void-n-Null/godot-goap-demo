using Godot;
using System;
using System.Collections.Generic;
using Game.Data.Components;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Game.Data;

/// <summary>
/// Base entity with core ECS functionality.
/// Supports component-based architecture with inheritable components.
/// </summary>
public class Entity : IUpdatableEntity
{
	/// <summary>
	/// Unique entity ID for component lookup and debugging.
	/// </summary>
	public Guid Id { get; } = EntityIdGenerator.Next();

	/// <summary>
	/// Direct index into the SpatialQuadTree's internal element array.
	/// Allows O(1) lookups for spatial updates, bypassing dictionary hashing.
	/// -1 means not tracked.
	/// </summary>
	public int SpatialHandle { get; set; } = -1;

	/// <summary>
	/// ID of the agent that has reserved this entity.
	/// Used for O(1) reservation checks without dictionary lookups.
	/// Guid.Empty means not reserved.
	/// </summary>
	public Guid ReservedByAgentId { get; set; } = Guid.Empty;

	/// <summary>
	/// Human-readable name of the entity, usually from its blueprint.
	/// </summary>
	public string Name { get; internal set; }

	public EntityBlueprint Blueprint { get; internal set; }

	/// <summary>
	/// Entity tags for categorization and filtering.
	/// </summary>
	public HashSet<Tag> Tags { get; } = [];

	private TransformComponent2D _transform;
	private VisualComponent _visual;
	public TransformComponent2D Transform => _transform ??= GetComponent<TransformComponent2D>();
	public VisualComponent Visual => _visual ??= GetComponent<VisualComponent>();

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
	/// Components attached to this entity, stored by Component ID.
	/// Replaces dictionary for O(1) access.
	/// </summary>
	protected IComponent[] _fastComponents = new IComponent[16];

	/// <summary>
	/// Active components that should receive per-frame updates.
	/// Stored in a list for tight iteration.
	/// </summary>
	protected readonly List<IActiveComponent> _activeComponents = [];

	/// <summary>
	/// True when this entity currently has at least one active component.
	/// </summary>
	public bool HasActiveComponents => _activeComponents.Count > 0;

	/// <summary>
	/// Fired when the active component presence toggles between zero and non-zero.
	/// Bool argument is the new state of <see cref="HasActiveComponents"/>.
	/// </summary>
	public event Action<Entity, bool> ActiveComponentsStateChanged;

	/// <summary>
	/// Tracks whether this entity has completed initial attachment.
	/// Controls whether newly added components should run OnPostAttached immediately.
	/// </summary>
	private bool _isInitialized;

	/// <summary>
	/// Gets a component of the specified type, or null if not found.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T GetComponent<T>() where T : class, IComponent
	{
		// Fast path: Check the slot for T
		int id = ComponentType<T>.Id;
		if (id < _fastComponents.Length)
		{
			var c = _fastComponents[id];
			if (c is T match) return match;
		}

		// Optimization: If T is sealed, it cannot be in any other slot (no subclasses).
		// So we can skip the fallback loop.
		if (ComponentType<T>.IsSealed) return null;

		// Polymorphic fallback: search for first component that implements T
		var comps = _fastComponents;
		for (int i = 0; i < comps.Length; i++)
		{
			if (comps[i] is T match) return match;
		}

		return null;
	}

	/// <summary>
	/// Attempts to get a component of the specified type.
	/// Returns true if found, false otherwise. The component is output via the out parameter.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetComponent<T>(out T component) where T : class, IComponent
	{
		// Fast path
		int id = ComponentType<T>.Id;
		if (id < _fastComponents.Length)
		{
			var c = _fastComponents[id];
			if (c is T match)
			{
				component = match;
				return true;
			}
		}

		if (ComponentType<T>.IsSealed)
		{
			component = null;
			return false;
		}

		// Polymorphic fallback
		var comps = _fastComponents;
		for (int i = 0; i < comps.Length; i++)
		{
			if (comps[i] is T match)
			{
				component = match;
				return true;
			}
		}

		component = null;
		return false;
	}

	/// <summary>
	/// Ensures the fast component array can hold the given ID.
	/// </summary>
	private void EnsureCapacity(int id)
	{
		if (id >= _fastComponents.Length)
		{
			int newSize = Math.Max(id + 1, _fastComponents.Length * 2);
			Array.Resize(ref _fastComponents, newSize);
		}
	}

	/// <summary>
	/// Adds or replaces a component using two-phase attachment.
	/// Uses the component's runtime type for storage.
	/// </summary>
	public void AddComponent<T>(T component) where T : IComponent
	{
		AddComponent((IComponent)component);
	}

	/// <summary>
	/// Adds or replaces a component when only the interface type is available at callsite.
	/// </summary>
	public void AddComponent(IComponent component)
	{
		bool wasActive = HasActiveComponents;
		var type = component.GetType();
		int id = ComponentRegistry.GetId(type);
		
		EnsureCapacity(id);

		var existing = _fastComponents[id];
		if (existing != null)
		{
			// Invalidate caches before detaching
			if (existing == _transform) _transform = null;
			if (existing == _visual) _visual = null;

			if (existing is IActiveComponent existingActive)
			{
				_activeComponents.Remove(existingActive);
			}
			existing.OnDetached();
			existing.Entity = null;
		}

		_fastComponents[id] = component;
		component.Entity = this;
		component.OnPreAttached();

		// Update caches for known component types
		if (component is TransformComponent2D addedTransform)
		{
			_transform = addedTransform;
		}
		else if (component is VisualComponent addedVisual)
		{
			_visual = addedVisual;
		}

		if (_isInitialized)
		{
			component.OnPostAttached();
			if (component is IActiveComponent active)
			{
				_activeComponents.Add(active);
			}
			if (!wasActive && HasActiveComponents)
			{
				ActiveComponentsStateChanged?.Invoke(this, true);
			}
			else if (wasActive && !HasActiveComponents)
			{
				ActiveComponentsStateChanged?.Invoke(this, false);
			}
		}
	}

	/// <summary>
	/// Completes component attachment after all components are added.
	/// Call this after adding all components to an entity.
	/// </summary>
	private void CompleteAttachment()
	{
		bool wasActive = HasActiveComponents;
		
		for (int i = 0; i < _fastComponents.Length; i++)
		{
			var component = _fastComponents[i];
			if (component == null) continue;

			// Skip if component was removed during initialization
			if (component.Entity != this) continue;

			component.OnPostAttached();
			if (component is IActiveComponent active)
			{
				_activeComponents.Add(active);
			}
		}

		if (!wasActive && HasActiveComponents)
		{
			ActiveComponentsStateChanged?.Invoke(this, true);
		}
	}

	/// <summary>
	/// Removes a component.
	/// </summary>
	public bool RemoveComponent<T>() where T : IComponent
	{
		int id = ComponentType<T>.Id;
		
		if (id < _fastComponents.Length && _fastComponents[id] != null)
		{
			return RemoveComponentInternal(id);
		}
		
		return false;
	}

	private bool RemoveComponentInternal(int id)
	{
		var component = _fastComponents[id];
		if (component == null) return false;

		bool wasActive = HasActiveComponents;

		// Invalidate caches for known component types BEFORE calling OnDetached
		if (component == _transform) _transform = null;
		if (component == _visual) _visual = null;

		component.OnDetached();
		component.Entity = null;
		if (component is IActiveComponent active)
		{
			_activeComponents.Remove(active);
		}
		
		_fastComponents[id] = null; // Clear slot

		if (wasActive && !HasActiveComponents)
		{
			ActiveComponentsStateChanged?.Invoke(this, false);
		}
		return true;
	}

	/// <summary>
	/// Checks if entity has a specific component.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool HasComponent<T>() where T : IComponent
	{
		// Fast path
		int id = ComponentType<T>.Id;
		if (id < _fastComponents.Length && _fastComponents[id] is T) return true;

		if (ComponentType<T>.IsSealed) return false;

		// Polymorphic fallback
		var comps = _fastComponents;
		for (int i = 0; i < comps.Length; i++)
		{
			if (comps[i] is T) return true;
		}
		return false;
	}

	/// <summary>
	/// Gets a component by a runtime type. Supports polymorphic matches.
	/// </summary>
	public IComponent GetComponent(Type componentType)
	{
		// Optimistic check: get ID for this type
		int id = ComponentRegistry.GetId(componentType);
		if (id < _fastComponents.Length)
		{
			var c = _fastComponents[id];
			if (c != null && componentType.IsAssignableFrom(c.GetType())) return c;
		}

		if (componentType.IsSealed) return null;

		// Fallback
		var comps = _fastComponents;
		for (int i = 0; i < comps.Length; i++)
		{
			var c = comps[i];
			if (c != null && componentType.IsAssignableFrom(c.GetType())) return c;
		}
		return null;
	}

	public void OnStart()
	{
		var comps = _fastComponents;
		for (int i = 0; i < comps.Length; i++)
		{
			comps[i]?.OnStart();
		}
	}

	/// <summary>
	/// Checks for a component by runtime type.
	/// </summary>
	public bool HasComponent(Type componentType) => GetComponent(componentType) != null;

	/// <summary>
	/// Enumerates all components attached to this entity without extra iterator allocations.
	/// </summary>
	public IEnumerable<IComponent> GetAllComponents()
	{
		var comps = _fastComponents;
		for (int i = 0; i < comps.Length; i++)
		{
			var component = comps[i];
			if (component != null)
			{
				yield return component;
			}
		}
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
		for (int i = 0; i < _fastComponents.Length; i++)
		{
			var component = _fastComponents[i];
			if (component != null)
			{
				component.OnDetached();
				_fastComponents[i] = null;
			}
		}
		_activeComponents.Clear();
		_transform = null;
		_visual = null;
	}

	// Note: Factory method moved to EntityFactory. Keep class minimal.
}
