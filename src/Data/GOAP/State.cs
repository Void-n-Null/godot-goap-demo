using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Game.Data.GOAP;

public class State
{
    private static readonly IReadOnlyDictionary<string, object> EmptyFacts = new Dictionary<string, object>();
    // For planning: layered state with parent + deltas (avoids full dict copies)
    // Note: Not readonly to allow materialization on first hash for performance
    private State _parent;
    private Dictionary<string, object> _delta;

    // For non-planning: direct facts dictionary
    private Dictionary<string, object> _directFacts;

    // Lazy-computed full facts view (only for IReadOnlyDictionary access)
    private IReadOnlyDictionary<string, object> _factsView;

    public IReadOnlyDictionary<string, object> Facts => _factsView ??= BuildFactsView();

    // Cache hash code for performance (computed lazily)
    private int? _cachedHash;

    public State(Dictionary<string, object> facts)
    {
        _directFacts = new Dictionary<string, object>(facts ?? new Dictionary<string, object>());
    }

    /// <summary>
    /// Internal constructor for layered states during planning.
    /// Stores only the delta from parent, avoiding full dictionary copies.
    /// </summary>
    internal State(State parent, Dictionary<string, object> delta)
    {
        _parent = parent;
        _delta = delta;
    }

    /// <summary>
    /// Internal constructor that takes ownership of the dictionary without copying.
    /// Use only when the caller guarantees the dictionary won't be mutated.
    /// </summary>
    internal State(Dictionary<string, object> facts, bool takeOwnership)
    {
        _directFacts = facts ?? new Dictionary<string, object>();
    }

    private IReadOnlyDictionary<string, object> BuildFactsView()
    {
        if (_directFacts != null)
            return _directFacts;

        // Layered state: merge parent + delta lazily without allocating a new dictionary when possible
        var parentFacts = _parent?.Facts ?? EmptyFacts;
        var delta = _delta;

        // If there is no delta, reuse parent's already-materialized facts view
        if (delta == null || delta.Count == 0)
            return parentFacts;

        return new LayeredFactsView(parentFacts, delta);
    }

    /// <summary>
    /// Fast lookup that traverses the layer chain without materializing full dictionary.
    /// </summary>
    public bool TryGetValue(string key, out object value)
    {
        if (_directFacts != null)
            return _directFacts.TryGetValue(key, out value);

        // Check delta first, then parent chain
        if (_delta != null && _delta.TryGetValue(key, out value))
            return true;

        return _parent?.TryGetValue(key, out value) ?? (value = null) == null;
    }

    /// <summary>
    /// Gets the cached deterministic hash for this state.
    /// Used by planning algorithms for visited state tracking.
    /// Uses pooled collections to avoid allocations.
    /// </summary>
    internal int GetDeterministicHash()
    {
        if (_cachedHash.HasValue)
            return _cachedHash.Value;

        // Direct facts - compute hash directly
        if (_directFacts != null)
        {
            if (_directFacts.Count == 0)
            {
                _cachedHash = 0;
                return 0;
            }

            _cachedHash = ComputeHashFromDictionary(_directFacts);
            return _cachedHash.Value;
        }

        // Layered state - collect keys efficiently and compute hash without full materialization
        _cachedHash = ComputeHashFromHierarchy();
        return _cachedHash.Value;
    }

    /// <summary>
    /// Computes hash for a direct dictionary using ArrayPool.
    /// </summary>
    private static int ComputeHashFromDictionary(Dictionary<string, object> facts)
    {
        int count = facts.Count;
        var pool = ArrayPool<string>.Shared;
        var keys = pool.Rent(count);

        try
        {
            int length = 0;
            foreach (var key in facts.Keys)
            {
                keys[length++] = key;
            }

            Array.Sort(keys, 0, length, StringComparer.Ordinal);

            int hash = 0;
            for (int i = 0; i < length; i++)
            {
                var key = keys[i];
                var value = facts[key];
                hash = HashCode.Combine(hash, key, value);
                keys[i] = null;
            }

            return hash;
        }
        finally
        {
            pool.Return(keys, clearArray: false);
        }
    }

    /// <summary>
    /// Computes hash from layered hierarchy using pooled collections.
    /// Uses HashSet for O(1) deduplication instead of O(n²) array scanning.
    /// </summary>
    private int ComputeHashFromHierarchy()
    {
        // Collect unique keys using HashSet (O(n) instead of O(n²))
        var keySet = new HashSet<string>(StringComparer.Ordinal);
        var state = this;

        while (state != null)
        {
            var source = state._directFacts ?? state._delta;
            if (source != null)
            {
                foreach (var key in source.Keys)
                {
                    keySet.Add(key); // O(1) deduplication
                }
            }
            state = state._parent;
        }

        if (keySet.Count == 0)
            return 0;

        // Sort keys and compute hash
        var pool = ArrayPool<string>.Shared;
        var keys = pool.Rent(keySet.Count);

        try
        {
            int length = 0;
            foreach (var key in keySet)
            {
                keys[length++] = key;
            }

            Array.Sort(keys, 0, length, StringComparer.Ordinal);

            int hash = 0;
            for (int i = 0; i < length; i++)
            {
                var key = keys[i];
                TryGetValue(key, out var value); // Traverse hierarchy for value
                hash = HashCode.Combine(hash, key, value);
                keys[i] = null;
            }

            return hash;
        }
        finally
        {
            pool.Return(keys, clearArray: false);
        }
    }

    public static State Empty() => new State(new Dictionary<string, object>());

    public State Clone()
    {
        return new State(new Dictionary<string, object>(Facts));
    }

    public bool Satisfies(State goal)
    {
        foreach (var goalFact in goal.Facts)
        {
            if (!TryGetValue(goalFact.Key, out var currentValue))
            {
                // For count facts, assume 0 if missing
                if (goalFact.Key.EndsWith("Count") && goalFact.Value is int)
                {
                    currentValue = 0;
                }
                else if (goalFact.Value is bool)
                {
                    // For boolean facts, assume false if missing
                    currentValue = false;
                }
                else
                {
                    return false;
                }
            }

            if (goalFact.Key.EndsWith("Count") && goalFact.Value is int goalCount && currentValue is int currentCount)
            {
                if (currentCount < goalCount) return false;
            }
            else if (!currentValue.Equals(goalFact.Value))
            {
                return false;
            }
        }
        return true;
    }

    // Equality and hash for planning (optional, for visited sets)
    public override bool Equals(object obj)
    {
        return obj is State other && Satisfies(other) && other.Satisfies(this);
    }

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var kvp in Facts.OrderBy(k => k.Key))
        {
            hash = hash * 23 + (kvp.Key?.GetHashCode() ?? 0);
            hash = hash * 23 + (kvp.Value?.GetHashCode() ?? 0);
        }
        return hash;
    }

    // Internal lightweight view that overlays a delta dictionary on top of a parent read-only dictionary.
    private sealed class LayeredFactsView : IReadOnlyDictionary<string, object>
    {
        private readonly IReadOnlyDictionary<string, object> _parent;
        private readonly Dictionary<string, object> _delta;
        private readonly int _count;

        public LayeredFactsView(IReadOnlyDictionary<string, object> parent, Dictionary<string, object> delta)
        {
            _parent = parent ?? EmptyFacts;
            _delta = delta ?? throw new ArgumentNullException(nameof(delta));

            int count = _parent.Count;
            count += _delta.Count;

            foreach (var key in _delta.Keys)
            {
                if (_parent.ContainsKey(key))
                {
                    count--;
                }
            }

            _count = count;
        }

        public int Count => _count;

        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var kvp in _delta)
                    yield return kvp.Key;

                foreach (var kvp in _parent)
                {
                    if (_delta.ContainsKey(kvp.Key))
                        continue;
                    yield return kvp.Key;
                }
            }
        }

        public IEnumerable<object> Values
        {
            get
            {
                foreach (var kvp in _delta)
                    yield return kvp.Value;

                foreach (var kvp in _parent)
                {
                    if (_delta.ContainsKey(kvp.Key))
                        continue;
                    yield return kvp.Value;
                }
            }
        }

        public object this[string key]
        {
            get
            {
                if (_delta.TryGetValue(key, out var value))
                    return value;

                if (_parent.TryGetValue(key, out value))
                    return value;

                throw new KeyNotFoundException(key);
            }
        }

        public bool ContainsKey(string key) => _delta.ContainsKey(key) || _parent.ContainsKey(key);

        public bool TryGetValue(string key, out object value)
        {
            if (_delta.TryGetValue(key, out value))
                return true;

            if (_parent.TryGetValue(key, out value))
                return true;

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var kvp in _delta)
                yield return kvp;

            foreach (var kvp in _parent)
            {
                if (_delta.ContainsKey(kvp.Key))
                    continue;
                yield return kvp;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
