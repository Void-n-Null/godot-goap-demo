using System;
using System.Collections.Generic;
using Game.Data;
using Godot;

namespace Game.Universe;

/// <summary>
/// Dynamic loose quadtree spatial index optimised for point-like entities with unknown world bounds.
/// Supports incremental inserts, removals, and spatial queries without requiring a predefined map size.
/// </summary>
public sealed class SpatialIndex
{
    private readonly EntityQuadTree _quadTree;

    public SpatialIndex(
        float initialExtent = 32768f,
        float minNodeSize = 32f,
        int maxItemsPerNode = 12,
        float looseFactor = 1.25f,
        int maxDepth = 12,
        Vector2? initialCenter = null)
    {
        _quadTree = new EntityQuadTree(initialExtent, minNodeSize, maxItemsPerNode, looseFactor, maxDepth, initialCenter);
    }

    public int TrackedEntityCount => _quadTree.TrackedEntityCount;

    public void ForEachNode(Action<Rect2, Rect2, int, int> visitor, bool includeEmpty = true)
    {
        _quadTree.ForEachNode(visitor, includeEmpty);
    }

    public void ForEachItem(Action<Vector2> visitor, int maxCount = int.MaxValue)
    {
        _quadTree.ForEachItem(visitor, maxCount);
    }

    public void Clear()
    {
        _quadTree.Clear();
    }

    public bool Untrack(Entity entity)
    {
        return _quadTree.Remove(entity);
    }

    public void Sync(Entity entity)
    {
        if (entity == null) return;
        var transform = entity.Transform;
        if (transform == null || !entity.IsActive)
        {
            Untrack(entity);
            return;
        }
        _quadTree.InsertOrUpdate(entity, transform.Position);
    }

    public List<Entity> QueryCircle(Vector2 center, float radius, Func<Entity, bool> predicate = null, int maxResults = int.MaxValue)
    {
        return _quadTree.QueryCircle(center, radius, predicate, maxResults);
    }

    public List<Entity> QueryRectangle(Rect2 area, Func<Entity, bool> predicate = null, int maxResults = int.MaxValue)
    {
        return _quadTree.QueryRectangle(area, predicate, maxResults);
    }

    public Entity FindNearest(Vector2 center, float maxRadius, Func<Entity, bool> predicate = null)
    {
        return _quadTree.FindNearest(center, maxRadius, predicate);
    }

		public List<Entity> QueryCircleWithCounts(Vector2 center, float radius, Func<Entity, bool> predicate, int maxResults, out int testedCount)
		{
			return _quadTree.QueryCircleWithCounts(center, radius, predicate, maxResults, out testedCount);
		}

    public Entity FindNearestWithCounts(Vector2 center, float maxRadius, Func<Entity, bool> predicate, out int testedCount)
    {
        return _quadTree.FindNearestWithCounts(center, maxRadius, predicate, out testedCount);
    }
}

