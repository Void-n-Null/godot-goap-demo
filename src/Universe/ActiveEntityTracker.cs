using System.Collections.Generic;
using Game.Data;

namespace Game.Universe;

/// <summary>
/// Tracks entities with active components that need per-frame updates.
/// Extracted from EntityManager to follow Single Responsibility Principle.
/// </summary>
public class ActiveEntityTracker
{
	private readonly List<IUpdatableEntity> _activeEntities = new();
	private readonly HashSet<IUpdatableEntity> _activeEntityLookup = new();

	public IReadOnlyList<IUpdatableEntity> ActiveEntities => _activeEntities;
	public int Count => _activeEntities.Count;

	/// <summary>
	/// Add an entity to active tracking
	/// </summary>
	public void Add(IUpdatableEntity entity)
	{
		if (entity == null) return;

		if (_activeEntityLookup.Add(entity))
		{
			_activeEntities.Add(entity);
		}
	}

	/// <summary>
	/// Remove an entity from active tracking
	/// </summary>
	public void Remove(IUpdatableEntity entity)
	{
		if (entity == null) return;

		if (_activeEntityLookup.Remove(entity))
		{
			_activeEntities.Remove(entity);
		}
	}

	/// <summary>
	/// Check if an entity is actively tracked
	/// </summary>
	public bool Contains(IUpdatableEntity entity)
	{
		return _activeEntityLookup.Contains(entity);
	}

	/// <summary>
	/// Clear all active entities
	/// </summary>
	public void Clear()
	{
		_activeEntities.Clear();
		_activeEntityLookup.Clear();
	}
}
