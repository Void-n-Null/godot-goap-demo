using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Game.Data;
using Godot;

namespace Game.Universe;

/// <summary>
/// High-performance Data-Oriented QuadTree.
/// Uses flat arrays and integer indexing to maximize cache locality and minimize GC pressure.
/// Entities store their own 'SpatialHandle' (index) for O(1) updates/removals.
/// </summary>
public sealed class EntityQuadTree
{
    private struct NodeData
    {
        // Hot Data (Accessed in Query/IntersectsCircle)
        // Pre-computed AABB bounds for zero-arithmetic intersection tests
        public float LooseMinX;
        public float LooseMinY;
        public float LooseMaxX;
        public float LooseMaxY;
        
        public int FirstChildIndex; // Index of first child (4 sequential nodes), or -1 if leaf
        public int FirstElementIndex; // Index of first element in linked list, or -1

        // Used in Expand/Subdivide/GetQuadrant (less hot in queries)
        public float CenterX;
        public float CenterY;
        public float HalfSize;

        // Metadata
        public int Count;
        public int Depth;
        public int ParentIndex;

        // Cold Data (Large structs, rarely used in hot loops)
        public Rect2 Bounds;
        public Rect2 LooseBounds;
    }

    private struct ElementData
    {
        public Entity Entity;
        public Vector2 Position;
        public int NextElementIndex; // Next element in the same node
        public int PrevElementIndex; // Previous element (doubly linked for O(1) removal)
        public int NodeIndex; // The node this element belongs to
    }

    private readonly float _minNodeSize;
    private readonly int _maxItemsPerNode;
    private readonly float _looseFactor;
    private readonly int _maxDepth;
    
    // Storage
    private NodeData[] _nodes;
    private int _nodeCount;
    
    private ElementData[] _elements;
    private int _elementCount;

    private int _firstFreeElement = -1;

    private int _rootIndex = -1;
    
    // Reusable query buffers
    private readonly int[] _nodeStack; // Reusable stack for queries
    private readonly List<Entity> _reusableResults = new(1024);
    private const int MaxQueryResults = 1024;
    private readonly List<int> _rebuildHandles = new(256);

    public int TrackedEntityCount => _activeItemCount;

    // Simple helper to count real items if needed, though usually we just track inserted count
    private int _activeItemCount = 0; 

    public EntityQuadTree(
        float initialExtent = 32768f,
        float minNodeSize = 32f,
        int maxItemsPerNode = 12,
        float looseFactor = 1.5f,
        int maxDepth = 12,
        Vector2? initialCenter = null)
    {
        _minNodeSize = minNodeSize;
        _maxItemsPerNode = maxItemsPerNode;
        _looseFactor = looseFactor;
        _maxDepth = maxDepth;

        // Initialize arrays with reasonable defaults to avoid immediate resizing
        _nodes = new NodeData[1024];
        _elements = new ElementData[2048];
        _nodeStack = new int[maxDepth * 4 + 32];

        Reset(initialCenter ?? Vector2.Zero, Mathf.Max(initialExtent * 0.5f, minNodeSize));
    }

    private void Reset(Vector2 center, float halfSize)
    {
        _nodeCount = 0;
        _elementCount = 0;
        _activeItemCount = 0;
        _firstFreeElement = -1;
        
        // Create root
        _rootIndex = AllocateNode();
        InitializeNode(_rootIndex, center, halfSize, 0, -1);
    }

    public void Clear()
    {
        // Just reset counters, no array clearing needed (O(1) clear)
        // We preserve the root dimensions though
        if (_rootIndex != -1)
        {
            var center = new Vector2(_nodes[_rootIndex].CenterX, _nodes[_rootIndex].CenterY);
            var halfSize = _nodes[_rootIndex].HalfSize;
            Reset(center, halfSize);
        }
    }

    public void InsertOrUpdate(Entity entity, Vector2 position)
    {
        if (entity == null) return;

        // CASE 1: Entity is already tracked
        if (entity.SpatialHandle != -1)
        {
            ref var el = ref _elements[entity.SpatialHandle];
            
            // If position hasn't changed enough to matter (still in same node loose bounds), update and return
            // Check if new position is within the *current node's loose bounds*
            if (el.NodeIndex != -1)
            {
                 ref var currentNode = ref _nodes[el.NodeIndex];
                 if (currentNode.LooseBounds.HasPoint(position))
                 {
                     el.Position = position; // Just update position
                     return; // FAST PATH EXIT
                 }
                 
                 // Moved outside node: Remove from current node, then fall through to re-insert
                 RemoveFromNode(entity.SpatialHandle, el.NodeIndex);
            }
        }
        else
        {
            // New entity allocation
            entity.SpatialHandle = AllocateElement();
            ref var newEl = ref _elements[entity.SpatialHandle];
            newEl.Entity = entity;
        }

        // Set new position
        ref var element = ref _elements[entity.SpatialHandle];
        element.Position = position;

        // Ensure root covers this point
        EnsureRootFor(position);

        // Insert into tree
        InsertElementRecursive(_rootIndex, entity.SpatialHandle);
    }

    public bool Remove(Entity entity)
    {
        if (entity == null || entity.SpatialHandle == -1) return false;

        int handle = entity.SpatialHandle;
        ref var el = ref _elements[handle];
        
        if (el.NodeIndex != -1)
        {
            RemoveFromNode(handle, el.NodeIndex);
        }

        FreeElement(handle);
        entity.SpatialHandle = -1;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InsertElementRecursive(int nodeIndex, int elementHandle)
    {
        // 1. If node has children, try to push down
        ref var node = ref _nodes[nodeIndex];
        
        if (node.FirstChildIndex != -1)
        {
            int quadrant = GetQuadrant(new Vector2(node.CenterX, node.CenterY), _elements[elementHandle].Position);
            int childIndex = node.FirstChildIndex + quadrant;
            
            // If child fully contains point (in loose bounds), go down
            ref var child = ref _nodes[childIndex];
            if (child.LooseBounds.HasPoint(_elements[elementHandle].Position))
            {
                InsertElementRecursive(childIndex, elementHandle);
                return;
            }
        }

        // 2. Must insert here (Leaf or doesn't fit in children)
        // Add to linked list
        int oldHead = node.FirstElementIndex;
        node.FirstElementIndex = elementHandle;
        
        ref var el = ref _elements[elementHandle];
        el.NodeIndex = nodeIndex;
        el.NextElementIndex = oldHead;
        el.PrevElementIndex = -1;
        
        if (oldHead != -1)
        {
            _elements[oldHead].PrevElementIndex = elementHandle;
        }

        node.Count++;

        // 3. Check subdivision
        if (node.FirstChildIndex == -1 && node.Count > _maxItemsPerNode && node.Depth < _maxDepth)
        {
            Subdivide(nodeIndex);
        }
    }

    private void Subdivide(int nodeIndex)
    {
        ref var node = ref _nodes[nodeIndex];
        
        // Allocate 4 children
        int firstChild = AllocateNode(); 
        AllocateNode(); AllocateNode(); AllocateNode(); // Reserve 3 more sequential

        // Re-fetch ref as array might have resized
        ref var nodeRef = ref _nodes[nodeIndex]; 
        nodeRef.FirstChildIndex = firstChild;

        float childHalf = nodeRef.HalfSize * 0.5f;
        int nextDepth = nodeRef.Depth + 1;

        // Init children
        for (int i = 0; i < 4; i++)
        {
            float childCenterX = nodeRef.CenterX + (((i & 1) == 0) ? -childHalf : childHalf);
            float childCenterY = nodeRef.CenterY + (((i & 2) == 0) ? -childHalf : childHalf);
            InitializeNode(firstChild + i, new Vector2(childCenterX, childCenterY), childHalf, nextDepth, nodeIndex);
        }

        // Distribute items
        int currentElIndex = nodeRef.FirstElementIndex;
        nodeRef.FirstElementIndex = -1;
        nodeRef.Count = 0;

        while (currentElIndex != -1)
        {
            int next = _elements[currentElIndex].NextElementIndex;
            // Re-insert (will go to children now)
            InsertElementRecursive(nodeIndex, currentElIndex);
            currentElIndex = next;
        }
    }

    private void RemoveFromNode(int elementHandle, int nodeIndex)
    {
        ref var node = ref _nodes[nodeIndex];
        ref var el = ref _elements[elementHandle];

        if (el.PrevElementIndex != -1)
        {
            _elements[el.PrevElementIndex].NextElementIndex = el.NextElementIndex;
        }
        else
        {
            // Was head
            node.FirstElementIndex = el.NextElementIndex;
        }

        if (el.NextElementIndex != -1)
        {
            _elements[el.NextElementIndex].PrevElementIndex = el.PrevElementIndex;
        }

        el.NodeIndex = -1;
        el.NextElementIndex = -1;
        el.PrevElementIndex = -1;
        node.Count--;
    }

    // ---------------- QUERIES ----------------

    /// <summary>
    /// Queries the tree for all entities within a circular region.
    /// WARNING: This method returns a SHARED BUFFER that is reused across all queries.
    /// The returned list is cleared and repopulated on every call to any Query method.
    /// DO NOT store the returned reference - copy the results if you need to keep them.
    /// DO NOT call this method from multiple threads or nested queries simultaneously.
    /// </summary>
    /// <example>
    /// // WRONG: Storing the reference
    /// var results = quadTree.QueryCircle(pos, radius);
    /// // ... later use results (will be corrupted by other queries)
    ///
    /// // CORRECT: Copy the results immediately
    /// var results = new List&lt;Entity&gt;(quadTree.QueryCircle(pos, radius));
    /// </example>
    public List<Entity> QueryCircle(Vector2 center, float radius, Func<Entity, bool> predicate = null, int maxResults = int.MaxValue)
    {
        if (_rootIndex == -1)
        {
            _reusableResults.Clear();
            return _reusableResults;
        }

        float rSq = radius * radius;
        bool checkRadius = !float.IsPositiveInfinity(radius);
        
        // Root check: if root doesn't intersect, return empty
        if (checkRadius && !IntersectsCircle(ref _nodes[_rootIndex], center, rSq))
        {
            _reusableResults.Clear();
            return _reusableResults;
        }

        // Clamp maxResults to buffer size and pre-allocate
        int effectiveMax = Math.Min(maxResults, MaxQueryResults);
        CollectionsMarshal.SetCount(_reusableResults, effectiveMax);
        var resultsSpan = CollectionsMarshal.AsSpan(_reusableResults);
        int resultCount = 0;
        
        int stackPtr = 0;
        _nodeStack[stackPtr++] = _rootIndex;
        
        // Cache center coordinates to avoid repeated struct field access
        float cx = center.X;
        float cy = center.Y;
        bool hasPredicate = predicate != null;

        while (stackPtr > 0 && resultCount < effectiveMax)
        {
            int nodeIdx = _nodeStack[--stackPtr];
            ref var node = ref _nodes[nodeIdx];
            
            // Iterate Elements
            int elIdx = node.FirstElementIndex;
            if (checkRadius)
            {
                while (elIdx != -1)
                {
                    ref var el = ref _elements[elIdx];
                    
                    // Check distance using cached position and center
                    float dx = el.Position.X - cx;
                    float dy = el.Position.Y - cy;
                    
                    if (dx*dx + dy*dy <= rSq)
                    {
                        if (!hasPredicate || predicate(el.Entity))
                        {
                            resultsSpan[resultCount++] = el.Entity;
                            if (resultCount >= effectiveMax) goto Done;
                        }
                    }
                    elIdx = el.NextElementIndex;
                }
            }
            else
            {
                while (elIdx != -1)
                {
                    ref var el = ref _elements[elIdx];
                    if (!hasPredicate || predicate(el.Entity))
                    {
                        resultsSpan[resultCount++] = el.Entity;
                        if (resultCount >= effectiveMax) goto Done;
                    }
                    elIdx = el.NextElementIndex;
                }
            }

            // Push Children - SIMD optimized (check all 4 at once)
            if (node.FirstChildIndex != -1)
            {
                int baseIdx = node.FirstChildIndex;
                
                if (checkRadius)
                {
                    // Load bounds from all 4 children
                    ref var c0 = ref _nodes[baseIdx];
                    ref var c1 = ref _nodes[baseIdx + 1];
                    ref var c2 = ref _nodes[baseIdx + 2];
                    ref var c3 = ref _nodes[baseIdx + 3];
                    
                    // Build vectors for parallel AABB-circle intersection
                    var minX = Vector128.Create(c0.LooseMinX, c1.LooseMinX, c2.LooseMinX, c3.LooseMinX);
                    var maxX = Vector128.Create(c0.LooseMaxX, c1.LooseMaxX, c2.LooseMaxX, c3.LooseMaxX);
                    var minY = Vector128.Create(c0.LooseMinY, c1.LooseMinY, c2.LooseMinY, c3.LooseMinY);
                    var maxY = Vector128.Create(c0.LooseMaxY, c1.LooseMaxY, c2.LooseMaxY, c3.LooseMaxY);
                    
                    // Broadcast query params
                    var cxVec = Vector128.Create(cx);
                    var cyVec = Vector128.Create(cy);
                    var rSqVec = Vector128.Create(rSq);
                    
                    // Clamp center to AABB: closest = max(min, min(max, center))
                    var closestX = Vector128.Max(minX, Vector128.Min(maxX, cxVec));
                    var closestY = Vector128.Max(minY, Vector128.Min(maxY, cyVec));
                    
                    // Distance squared from center to closest point
                    var dx = cxVec - closestX;
                    var dy = cyVec - closestY;
                    var distSq = dx * dx + dy * dy;
                    
                    // Compare: distSq <= rSq (result is all 1s or all 0s per lane)
                    var mask = Vector128.LessThanOrEqual(distSq, rSqVec);
                    
                    // Extract and push intersecting children
                    // GetElement returns the float bits; non-zero means intersection
                    if (mask.GetElement(0) != 0) _nodeStack[stackPtr++] = baseIdx;
                    if (mask.GetElement(1) != 0) _nodeStack[stackPtr++] = baseIdx + 1;
                    if (mask.GetElement(2) != 0) _nodeStack[stackPtr++] = baseIdx + 2;
                    if (mask.GetElement(3) != 0) _nodeStack[stackPtr++] = baseIdx + 3;
                }
                else
                {
                    // No radius check - push all children
                    _nodeStack[stackPtr++] = baseIdx;
                    _nodeStack[stackPtr++] = baseIdx + 1;
                    _nodeStack[stackPtr++] = baseIdx + 2;
                    _nodeStack[stackPtr++] = baseIdx + 3;
                }
            }
        }

        Done:
        // Trim to actual count
        CollectionsMarshal.SetCount(_reusableResults, resultCount);
        return _reusableResults;
    }

    public List<Entity> QueryCircleWithCounts(Vector2 center, float radius, Func<Entity, bool> predicate, int maxResults, out int testedCount)
    {
        testedCount = 0;
        _reusableResults.Clear();
        if (_rootIndex == -1) return _reusableResults;

        float rSq = radius * radius;
        bool checkRadius = !float.IsPositiveInfinity(radius);
        
        if (checkRadius && !IntersectsCircle(ref _nodes[_rootIndex], center, rSq))
        {
            return _reusableResults;
        }
        
        int stackPtr = 0;
        _nodeStack[stackPtr++] = _rootIndex;

        while (stackPtr > 0 && _reusableResults.Count < maxResults)
        {
            int nodeIdx = _nodeStack[--stackPtr];
            ref var node = ref _nodes[nodeIdx];

            // Iterate Elements
            int elIdx = node.FirstElementIndex;
            if (checkRadius)
            {
                while (elIdx != -1)
                {
                    ref var el = ref _elements[elIdx];
                    testedCount++;

                    float dx = el.Position.X - center.X;
                    float dy = el.Position.Y - center.Y;
                    
                    if (dx*dx + dy*dy <= rSq)
                    {
                        if (predicate == null || predicate(el.Entity))
                        {
                            _reusableResults.Add(el.Entity);
                            if (_reusableResults.Count >= maxResults) return _reusableResults;
                        }
                    }
                    elIdx = el.NextElementIndex;
                }
            }
            else
            {
                while (elIdx != -1)
                {
                    ref var el = ref _elements[elIdx];
                    testedCount++;
                    if (predicate == null || predicate(el.Entity))
                    {
                        _reusableResults.Add(el.Entity);
                        if (_reusableResults.Count >= maxResults) return _reusableResults;
                    }
                    elIdx = el.NextElementIndex;
                }
            }

            // Push Children
            if (node.FirstChildIndex != -1)
            {
                for (int i = 0; i < 4; i++)
                {
                    int childIdx = node.FirstChildIndex + i;
                    ref var child = ref _nodes[childIdx];
                    if (!checkRadius || IntersectsCircle(ref child, center, rSq))
                    {
                         _nodeStack[stackPtr++] = childIdx;
                    }
                }
            }
        }

        return _reusableResults;
    }

    /// <summary>
    /// Queries the tree for all entities within a rectangular region.
    /// WARNING: This method returns a SHARED BUFFER that is reused across all queries.
    /// The returned list is cleared and repopulated on every call to any Query method.
    /// DO NOT store the returned reference - copy the results if you need to keep them.
    /// DO NOT call this method from multiple threads or nested queries simultaneously.
    /// </summary>
    /// <example>
    /// // WRONG: Storing the reference
    /// var results = quadTree.QueryRectangle(rect);
    /// // ... later use results (will be corrupted by other queries)
    ///
    /// // CORRECT: Copy the results immediately
    /// var results = new List&lt;Entity&gt;(quadTree.QueryRectangle(rect));
    /// </example>
    public List<Entity> QueryRectangle(Rect2 area, Func<Entity, bool> predicate = null, int maxResults = int.MaxValue)
    {
        _reusableResults.Clear();
        if (_rootIndex == -1) return _reusableResults;

        int stackPtr = 0;
        _nodeStack[stackPtr++] = _rootIndex;

        while (stackPtr > 0 && _reusableResults.Count < maxResults)
        {
            int nodeIdx = _nodeStack[--stackPtr];
            ref var node = ref _nodes[nodeIdx];

            // Rectangle query still does check-on-pop because we use Intersects(Rect2) which is fast.
            // But we could optimize it similarly.
            if (!node.LooseBounds.Intersects(area)) continue;

            int elIdx = node.FirstElementIndex;
            while (elIdx != -1)
            {
                ref var el = ref _elements[elIdx];
                
                if (area.HasPoint(el.Position) && (predicate == null || predicate(el.Entity)))
                {
                    _reusableResults.Add(el.Entity);
                    if (_reusableResults.Count >= maxResults) return _reusableResults;
                }
                elIdx = el.NextElementIndex;
            }

            if (node.FirstChildIndex != -1)
            {
                for (int i = 0; i < 4; i++)
                {
                    int childIdx = node.FirstChildIndex + i;
                    // Optimization: Check before push
                    if (_nodes[childIdx].LooseBounds.Intersects(area))
                    {
                        _nodeStack[stackPtr++] = childIdx;
                    }
                }
            }
        }

        return _reusableResults;
    }

    /// <summary>
    /// Find the nearest entity within a maximum search radius.
    /// NOTE: This method uses the same shared buffer as QueryCircle/QueryRectangle internally.
    /// However, it returns a single Entity, so the buffer reuse is not a concern for callers.
    /// </summary>
    public Entity FindNearest(Vector2 position, float maxRadius, Func<Entity, bool> predicate = null)
    {
        return FindNearestWithCounts(position, maxRadius, predicate, out _);
    }

    public Entity FindNearestWithCounts(Vector2 center, float maxRadius, Func<Entity, bool> predicate, out int testedCount)
    {
        testedCount = 0;
        if (_rootIndex == -1) return null;

        float bestDistSq = float.IsPositiveInfinity(maxRadius) ? float.PositiveInfinity : maxRadius * maxRadius;
        Entity bestEntity = null;

        int stackPtr = 0;
        _nodeStack[stackPtr++] = _rootIndex;

        while (stackPtr > 0)
        {
            int nodeIdx = _nodeStack[--stackPtr];
            ref var node = ref _nodes[nodeIdx];

            // Pruning: If node is further than current best distance, skip
            // We check this on pop because bestDistSq SHRINKS during traversal.
            // A node pushed earlier might now be invalid.
            if (!IntersectsCircle(ref node, center, bestDistSq)) continue;

            // Check Items
            int elIdx = node.FirstElementIndex;
            while (elIdx != -1)
            {
                ref var el = ref _elements[elIdx];
                testedCount++;
                
                // Distance check first (using cached position)
                float dx = el.Position.X - center.X;
                float dy = el.Position.Y - center.Y;
                float dSq = dx*dx + dy*dy;

                if (dSq < bestDistSq)
                {
                    if (predicate == null || predicate(el.Entity))
                    {
                        bestDistSq = dSq;
                        bestEntity = el.Entity;
                    }
                }
                elIdx = el.NextElementIndex;
            }

            // Push children
            if (node.FirstChildIndex != -1)
            {
                for (int i = 0; i < 4; i++)
                {
                    int childIdx = node.FirstChildIndex + i;
                    if (IntersectsCircle(ref _nodes[childIdx], center, bestDistSq))
                    {
                        _nodeStack[stackPtr++] = childIdx;
                    }
                }
            }
        }

        return bestEntity;
    }
    
    // ---------------- UTILS ----------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IntersectsCircle(ref NodeData node, Vector2 center, float radiusSq)
    {
        // Circle vs pre-computed loose AABB - find closest point on box to circle center
        float cx = center.X;
        float cy = center.Y;
        float closestX = cx < node.LooseMinX ? node.LooseMinX : (cx > node.LooseMaxX ? node.LooseMaxX : cx);
        float closestY = cy < node.LooseMinY ? node.LooseMinY : (cy > node.LooseMaxY ? node.LooseMaxY : cy);
        float dx = cx - closestX;
        float dy = cy - closestY;
        return (dx * dx + dy * dy) <= radiusSq;
    }

    private int GetQuadrant(Vector2 nodeCenter, Vector2 point)
    {
        int q = 0;
        if (point.X >= nodeCenter.X) q |= 1;
        if (point.Y >= nodeCenter.Y) q |= 2;
        return q;
    }

    private void EnsureRootFor(Vector2 point)
    {
        if (_rootIndex == -1)
        {
            Reset(point, _minNodeSize * 8f);
            return;
        }

        var center = new Vector2(_nodes[_rootIndex].CenterX, _nodes[_rootIndex].CenterY);
        float half = _nodes[_rootIndex].HalfSize;

        while (!ContainsPoint(center, half, point))
        {
            half *= 2f;
        }

        if (half > _nodes[_rootIndex].HalfSize) // Did it grow?
        {
            RebuildTree(center, half);
        }
    }

    private void RebuildTree(Vector2 center, float halfSize)
    {
        _rebuildHandles.Clear();
        for (int i = 0; i < _elementCount; i++)
        {
            if (_elements[i].Entity == null) continue;
            ref var el = ref _elements[i];
            el.NodeIndex = -1;
            el.NextElementIndex = -1;
            el.PrevElementIndex = -1;
            _rebuildHandles.Add(i);
        }

        _nodeCount = 0;
        _rootIndex = AllocateNode();
        InitializeNode(_rootIndex, center, halfSize, 0, -1);

        for (int i = 0; i < _rebuildHandles.Count; i++)
        {
            InsertElementRecursive(_rootIndex, _rebuildHandles[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InitializeNode(int index, Vector2 center, float halfSize, int depth, int parentIndex)
    {
        ref var node = ref _nodes[index];
        var clampedHalf = Mathf.Max(halfSize, _minNodeSize);
        node.CenterX = center.X;
        node.CenterY = center.Y;
        node.HalfSize = clampedHalf;
        
        // Pre-compute loose AABB bounds for zero-arithmetic intersection tests
        float looseHalf = clampedHalf * _looseFactor;
        node.LooseMinX = center.X - looseHalf;
        node.LooseMinY = center.Y - looseHalf;
        node.LooseMaxX = center.X + looseHalf;
        node.LooseMaxY = center.Y + looseHalf;

        Vector2 halfVec = new(clampedHalf, clampedHalf);
        node.Bounds = new Rect2(center - halfVec, halfVec * 2f);

        Vector2 looseHalfVec = new(looseHalf, looseHalf);
        node.LooseBounds = new Rect2(center - looseHalfVec, looseHalfVec * 2f);

        node.Depth = depth;
        node.ParentIndex = parentIndex;
        node.FirstChildIndex = -1;
        node.FirstElementIndex = -1;
        node.Count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsPoint(Vector2 center, float half, Vector2 point)
    {
        return point.X >= center.X - half &&
               point.X <= center.X + half &&
               point.Y >= center.Y - half &&
               point.Y <= center.Y + half;
    }

    // ---------------- MEMORY ----------------

    private int AllocateNode()
    {
        if (_nodeCount >= _nodes.Length)
        {
            Array.Resize(ref _nodes, _nodes.Length * 2);
        }
        return _nodeCount++;
    }

    private int AllocateElement()
    {
        int index;
        if (_firstFreeElement != -1)
        {
            index = _firstFreeElement;
            _firstFreeElement = _elements[index].NextElementIndex; // Pop free list
        }
        else
        {
            if (_elementCount >= _elements.Length)
            {
                Array.Resize(ref _elements, _elements.Length * 2);

            }
            index = _elementCount++;
        }
        
        _activeItemCount++;
        // Reset data
        _elements[index].NextElementIndex = -1;
        _elements[index].PrevElementIndex = -1;
        _elements[index].NodeIndex = -1;
        return index;
    }

    private void FreeElement(int index)
    {
        _elements[index].Entity = null; // Clear ref to allow GC
        _elements[index].NextElementIndex = _firstFreeElement;
        _firstFreeElement = index;
        _activeItemCount--;
    }

    private int CountFreeElements()
    {
        int c = 0;
        int cur = _firstFreeElement;
        while (cur != -1)
        {
            c++;
            cur = _elements[cur].NextElementIndex;
        }
        return c;
    }
    
    public void ForEachNode(Action<Rect2, Rect2, int, int> visitor, bool includeEmpty = true)
    {
         if (_rootIndex == -1) return;
         
         int stackPtr = 0;
         _nodeStack[stackPtr++] = _rootIndex;
         
         while (stackPtr > 0)
         {
             int idx = _nodeStack[--stackPtr];
             ref var node = ref _nodes[idx];
             
             if (includeEmpty || node.Count > 0)
             {
                 visitor(node.Bounds, node.LooseBounds, node.Depth, node.Count);
             }
             
             if (node.FirstChildIndex != -1)
             {
                 for(int i=0; i<4; i++) _nodeStack[stackPtr++] = node.FirstChildIndex + i;
             }
         }
    }

    public void ForEachItem(Action<Vector2> visitor, int maxCount = int.MaxValue)
    {
        int visited = 0;
        for (int i = 0; i < _elementCount; i++)
        {
             if (_elements[i].Entity != null)
             {
                 visitor(_elements[i].Position);
                 visited++;
                 if (visited >= maxCount) break;
             }
        }
    }
}
