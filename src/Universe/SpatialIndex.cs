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
	private readonly float _minNodeSize;
	private readonly int _maxItemsPerNode;
	private readonly float _looseFactor;
	private readonly int _maxDepth;
	private readonly float _initialHalfExtent;
	private readonly Vector2 _initialCenter;

	private Node _root;
	private readonly Dictionary<Guid, Item> _items = new();

	public SpatialIndex(
		float initialExtent = 32768f,
		float minNodeSize = 32f,
		int maxItemsPerNode = 12,
		float looseFactor = 1.5f,
		int maxDepth = 12,
		Vector2? initialCenter = null)
	{
		if (initialExtent <= 0f) throw new ArgumentOutOfRangeException(nameof(initialExtent));
		if (minNodeSize <= 0f) throw new ArgumentOutOfRangeException(nameof(minNodeSize));
		if (maxItemsPerNode < 1) throw new ArgumentOutOfRangeException(nameof(maxItemsPerNode));
		if (looseFactor < 1f) throw new ArgumentOutOfRangeException(nameof(looseFactor));
		if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));

		_minNodeSize = minNodeSize;
		_maxItemsPerNode = maxItemsPerNode;
		_looseFactor = looseFactor;
		_maxDepth = maxDepth;
		_initialHalfExtent = Mathf.Max(initialExtent * 0.5f, minNodeSize);
		_initialCenter = initialCenter ?? Vector2.Zero;
	}

	public int TrackedEntityCount => _items.Count;

	/// <summary>
	/// Visits each node in the tree, providing its tight bounds, loose bounds, depth, and item count.
	/// </summary>
	public void ForEachNode(Action<Rect2, Rect2, int, int> visitor, bool includeEmpty = true)
	{
		if (visitor == null || _root == null) return;
		var stack = new Stack<Node>();
		stack.Push(_root);
		while (stack.Count > 0)
		{
			var node = stack.Pop();
			int count = node.Items.Count;
			if (includeEmpty || count > 0)
			{
				visitor(node.Bounds, node.LooseBounds, node.Depth, count);
			}
			if (node.Children != null)
			{
				for (int i = 0; i < node.Children.Length; i++)
				{
					var child = node.Children[i];
					if (child != null) stack.Push(child);
				}
			}
		}
	}

	/// <summary>
	/// Visits each tracked item position.
	/// </summary>
	public void ForEachItem(Action<Vector2> visitor, int maxCount = int.MaxValue)
	{
		if (visitor == null || _items.Count == 0) return;
		int visited = 0;
		foreach (var kv in _items)
		{
			visitor(kv.Value.Position);
			visited++;
			if (visited >= maxCount) break;
		}
	}

	public void Clear()
	{
		_items.Clear();
		_root = null;
	}

	public bool Untrack(Entity entity)
	{
		if (entity == null) return false;
		if (!_items.Remove(entity.Id, out var item))
		{
			return false;
		}

		item.Node?.Remove(item);
		item.Node = null;
		return true;
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

		var position = transform.Position;
		if (_items.TryGetValue(entity.Id, out var existing))
		{
			if (existing.Node == null)
			{
				existing.Position = position;
				EnsureRootFor(position);
				_root.Insert(existing);
				return;
			}

			existing.Position = position;
			if (!existing.Node.LooseContains(position))
			{
				existing.Node.Remove(existing);
				EnsureRootFor(position);
				_root.Insert(existing);
			}
			return;
		}

		var item = new Item(entity, position);
		_items.Add(entity.Id, item);
		EnsureRootFor(position);
		_root.Insert(item);
	}

	public List<Entity> QueryCircle(Vector2 center, float radius, Func<Entity, bool> predicate = null, int maxResults = int.MaxValue)
	{
		var results = new List<Entity>();
		if (_root == null || radius < 0f || maxResults <= 0)
		{
			return results;
		}

		var searchRadius = radius <= 0f || float.IsInfinity(radius) ? float.PositiveInfinity : radius;
		var radiusSquared = float.IsPositiveInfinity(searchRadius) ? float.PositiveInfinity : searchRadius * searchRadius;

		SpanQuery(center, radiusSquared, predicate, maxResults, results, QueryShape.Circle);
		return results;
	}

	public List<Entity> QueryRectangle(Rect2 area, Func<Entity, bool> predicate = null, int maxResults = int.MaxValue)
	{
		var results = new List<Entity>();
		if (_root == null || maxResults <= 0)
		{
			return results;
		}

		SpanQuery(area, predicate, maxResults, results);
		return results;
	}

	public Entity FindNearest(Vector2 center, float maxRadius, Func<Entity, bool> predicate = null)
	{
		if (_root == null) return null;

		var radius = maxRadius <= 0f || float.IsInfinity(maxRadius)
			? float.PositiveInfinity
			: maxRadius;
		var radiusSquared = float.IsPositiveInfinity(radius) ? float.PositiveInfinity : radius * radius;

		Entity closest = null;
		var bestDistSq = radiusSquared;

		var stack = new Stack<Node>();
		stack.Push(_root);

		while (stack.Count > 0)
		{
			var node = stack.Pop();
			if (!node.IntersectsCircle(center, bestDistSq))
				continue;

			for (int i = 0; i < node.Items.Count; i++)
			{
				var item = node.Items[i];
				var candidate = item.Entity;
				var candidateTransform = candidate?.Transform;
				if (candidateTransform == null)
					continue;
				if (predicate != null && !predicate(candidate))
					continue;
				var distSq = candidateTransform.Position.DistanceSquaredTo(center);
				if (distSq >= bestDistSq)
					continue;
				closest = candidate;
				bestDistSq = distSq;
			}

			if (node.Children != null)
			{
				for (int i = 0; i < node.Children.Length; i++)
				{
					var child = node.Children[i];
					if (child == null)
						continue;
					if (!child.IntersectsCircle(center, bestDistSq))
						continue;
					stack.Push(child);
				}
			}
		}

		// If we had an explicit max radius and best distance exceeds it, treat as not found
		if (!float.IsPositiveInfinity(radius) && closest != null)
		{
			var actualDistSq = closest.Transform.Position.DistanceSquaredTo(center);
			if (actualDistSq > radiusSquared)
			{
				return null;
			}
		}

		return closest;
	}

	private void SpanQuery(Vector2 center, float radiusSquared, Func<Entity, bool> predicate, int maxResults, List<Entity> results, QueryShape shape)
	{
		var stack = new Stack<Node>();
		stack.Push(_root);

		while (stack.Count > 0 && results.Count < maxResults)
		{
			var node = stack.Pop();
			if (shape == QueryShape.Circle)
			{
				if (!node.IntersectsCircle(center, radiusSquared))
					continue;
			}

			for (int i = 0; i < node.Items.Count; i++)
			{
				if (results.Count >= maxResults)
					return;

				var item = node.Items[i];
				var entity = item.Entity;
				var transform = entity?.Transform;
				if (transform == null)
					continue;
				if (predicate != null && !predicate(entity))
					continue;

				if (shape == QueryShape.Circle && !float.IsPositiveInfinity(radiusSquared))
				{
					var distSq = transform.Position.DistanceSquaredTo(center);
					if (distSq > radiusSquared)
						continue;
				}

				results.Add(entity);
			}

			if (node.Children == null)
				continue;

			for (int i = 0; i < node.Children.Length; i++)
			{
				var child = node.Children[i];
				if (child == null)
					continue;
				if (shape == QueryShape.Circle)
				{
					if (!child.IntersectsCircle(center, radiusSquared))
						continue;
				}
				stack.Push(child);
			}
		}
	}

	private void SpanQuery(Rect2 area, Func<Entity, bool> predicate, int maxResults, List<Entity> results)
	{
		var stack = new Stack<Node>();
		stack.Push(_root);

		while (stack.Count > 0 && results.Count < maxResults)
		{
			var node = stack.Pop();
			if (!node.IntersectsRect(area))
				continue;

			for (int i = 0; i < node.Items.Count; i++)
			{
				if (results.Count >= maxResults)
					return;

				var item = node.Items[i];
				var entity = item.Entity;
				var transform = entity?.Transform;
				if (transform == null)
					continue;
				if (predicate != null && !predicate(entity))
					continue;
				if (!area.HasPoint(transform.Position))
					continue;
				results.Add(entity);
			}

			if (node.Children == null)
				continue;

			for (int i = 0; i < node.Children.Length; i++)
			{
				var child = node.Children[i];
				if (child == null)
					continue;
				if (!child.IntersectsRect(area))
					continue;
				stack.Push(child);
			}
		}
	}

	private void EnsureRootFor(Vector2 position)
	{
		if (_root == null)
		{
			_root = new Node(_initialCenter, _initialHalfExtent, 0, null, this);
		}

		if (_root.Contains(position))
		{
			return;
		}

		ExpandRoot(position);
	}

	private void ExpandRoot(Vector2 requiredPoint)
	{
		if (_root == null)
		{
			_root = new Node(_initialCenter, _initialHalfExtent, 0, null, this);
		}

		var snapshot = new List<Item>(_items.Values);
		var newHalf = _root.HalfSize;
		while (!Node.ContainsPoint(_root.Center, newHalf, requiredPoint))
		{
			newHalf *= 2f;
		}

		_root = new Node(_root.Center, newHalf, 0, null, this);

		for (int i = 0; i < snapshot.Count; i++)
		{
			var item = snapshot[i];
			item.Node = null;
		}

		for (int i = 0; i < snapshot.Count; i++)
		{
			_root.Insert(snapshot[i]);
		}
	}

	private enum QueryShape
	{
		Circle,
	}

	private sealed class Item
	{
		public readonly Entity Entity;
		public Vector2 Position;
		public Node Node;

		public Item(Entity entity, Vector2 position)
		{
			Entity = entity;
			Position = position;
		}
	}

	private sealed class Node
	{
		private readonly SpatialIndex _index;

		public Vector2 Center { get; }
		public float HalfSize { get; }
		public int Depth { get; }
		public Rect2 Bounds { get; }
		public Rect2 LooseBounds { get; }
		public Node Parent { get; }
		public List<Item> Items { get; }
		public Node[] Children { get; private set; }

		public Node(Vector2 center, float halfSize, int depth, Node parent, SpatialIndex index)
		{
			Center = center;
			HalfSize = Mathf.Max(halfSize, index._minNodeSize);
			Depth = depth;
			Parent = parent;
			_index = index;
			Bounds = CreateBounds(Center, HalfSize);
			LooseBounds = CreateBounds(Center, HalfSize * index._looseFactor);
			Items = new List<Item>(index._maxItemsPerNode + 1);
		}

		public void Insert(Item item)
		{
			if (Children != null)
			{
				var quadrant = GetQuadrant(item.Position);
				var child = Children[quadrant];
				if (child == null && CanCreateChild())
				{
					child = CreateChild(quadrant);
				}

				if (child != null && child.Contains(item.Position))
				{
					child.Insert(item);
					return;
				}
			}

			Items.Add(item);
			item.Node = this;

			if (ShouldSubdivide())
			{
				Subdivide();
			}
		}

		public void Remove(Item item)
		{
			if (!Items.Remove(item))
			{
				return;
			}
			item.Node = null;
			TryCollapseUpwards();
		}

		public bool Contains(Vector2 point) => Bounds.HasPoint(point);

		public bool LooseContains(Vector2 point) => LooseBounds.HasPoint(point);

		public bool IntersectsCircle(Vector2 center, float radiusSquared)
		{
			if (float.IsPositiveInfinity(radiusSquared))
			{
				return true;
			}

			var closestX = Mathf.Clamp(center.X, Bounds.Position.X, Bounds.Position.X + Bounds.Size.X);
			var closestY = Mathf.Clamp(center.Y, Bounds.Position.Y, Bounds.Position.Y + Bounds.Size.Y);
			var dx = center.X - closestX;
			var dy = center.Y - closestY;
			return dx * dx + dy * dy <= radiusSquared;
		}

		public bool IntersectsRect(Rect2 area)
		{
			return Bounds.Intersects(area);
		}

		private void TryCollapseUpwards()
		{
			var current = this;
			while (current != null)
			{
				if (!current.TryCollapse())
				{
					break;
				}
				current = current.Parent;
			}
		}

		private bool TryCollapse()
		{
			if (Children == null)
			{
				return Items.Count == 0 && Parent != null && Parent.Children != null;
			}

			var totalItems = Items.Count;
			for (int i = 0; i < Children.Length; i++)
			{
				var child = Children[i];
				if (child == null)
					continue;
				if (child.Children != null)
				{
					return false;
				}
				totalItems += child.Items.Count;
				if (totalItems > _index._maxItemsPerNode)
				{
					return false;
				}
			}

			for (int i = 0; i < Children.Length; i++)
			{
				var child = Children[i];
				if (child == null)
					continue;
				for (int j = 0; j < child.Items.Count; j++)
				{
					var item = child.Items[j];
					Items.Add(item);
					item.Node = this;
				}
				child.Items.Clear();
				Children[i] = null;
			}

			Children = null;
			return Parent != null;
		}

		private bool CanCreateChild()
		{
			if (Depth + 1 > _index._maxDepth)
			{
				return false;
			}
			return HalfSize * 0.5f >= _index._minNodeSize;
		}

		private bool ShouldSubdivide()
		{
			if (Children != null)
			{
				return false;
			}
			if (Items.Count <= _index._maxItemsPerNode)
			{
				return false;
			}
			return CanCreateChild();
		}

		private void Subdivide()
		{
			Children ??= new Node[4];
			for (int i = Items.Count - 1; i >= 0; i--)
			{
				var item = Items[i];
				var quadrant = GetQuadrant(item.Position);
				var child = Children[quadrant];
				if (child == null)
				{
					child = CreateChild(quadrant);
				}

				if (child == null)
				{
					continue;
				}

				if (child.Contains(item.Position))
				{
					Items.RemoveAt(i);
					child.Insert(item);
				}
			}
		}

		private Node CreateChild(int quadrant)
		{
			if (!CanCreateChild())
			{
				return null;
			}

			var offset = HalfSize * 0.5f;
			var childCenter = new Vector2(
				Center.X + (((quadrant & 1) == 1) ? offset : -offset),
				Center.Y + (((quadrant & 2) == 2) ? offset : -offset));

			var child = new Node(childCenter, HalfSize * 0.5f, Depth + 1, this, _index);
			Children ??= new Node[4];
			Children[quadrant] = child;
			return child;
		}

		private int GetQuadrant(Vector2 point)
		{
			var quadrant = 0;
			if (point.X >= Center.X)
			{
				quadrant |= 1;
			}
			if (point.Y >= Center.Y)
			{
				quadrant |= 2;
			}
			return quadrant;
		}

		private static Rect2 CreateBounds(Vector2 center, float halfSize)
		{
			var size = new Vector2(halfSize * 2f, halfSize * 2f);
			return new Rect2(center - size * 0.5f, size);
		}

		public static bool ContainsPoint(Vector2 center, float halfSize, Vector2 point)
		{
			var bounds = CreateBounds(center, halfSize);
			return bounds.HasPoint(point);
		}
	}
}

