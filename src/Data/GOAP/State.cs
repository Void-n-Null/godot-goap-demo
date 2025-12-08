using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Game.Data.GOAP;

public class State : IEnumerable<KeyValuePair<string, FactValue>>
{
    /// <summary>
    /// Backing storage with copy-on-write semantics to make Clone() cheap.
    /// When a clone is created, the buffer is marked shared; any mutation first
    /// copies the buffer so readers stay isolated.
    /// </summary>
    private sealed class Buffer
    {
        public FactValue[] Facts;
        /// <summary>
        /// Explicit list of active fact IDs for O(n) iteration instead of O(capacity).
        /// Maintained in sorted order for deterministic hash computation and O(log n) lookups.
        /// Replaces BitArray for existence checking - BinarySearch is fast for small lists.
        /// </summary>
        public List<int> ActiveIds;
        public int? CachedHash;
        public bool IsShared;

        public Buffer(int capacity)
        {
            Facts = new FactValue[capacity];
            ActiveIds = new List<int>(16); // Pre-allocate for typical state size
            CachedHash = null;
            IsShared = false;
        }

        private Buffer(FactValue[] facts, List<int> activeIds)
        {
            Facts = facts;
            ActiveIds = activeIds;
            CachedHash = null;
            IsShared = false;
        }

        public Buffer CloneDeep()
        {
            // Only copy active facts instead of entire array
            // Typical state has ~10-15 active facts vs 64+ capacity
            var factsCopy = new FactValue[Facts.Length];
            var activeIds = ActiveIds;
            var count = activeIds.Count;
            
            // Copy only the slots that have actual data
            for (int i = 0; i < count; i++)
            {
                int id = activeIds[i];
                factsCopy[id] = Facts[id];
            }
            
            // Fast list copy using Span - avoids IEnumerable overhead of AddRange
            var activeIdsCopy = new List<int>(count);
            if (count > 0)
            {
                CollectionsMarshal.SetCount(activeIdsCopy, count);
                var srcSpan = CollectionsMarshal.AsSpan(ActiveIds);
                var dstSpan = CollectionsMarshal.AsSpan(activeIdsCopy);
                srcSpan.CopyTo(dstSpan);
            }
            
            return new Buffer(factsCopy, activeIdsCopy);
        }
        
        /// <summary>
        /// O(log n) existence check using sorted ActiveIds list.
        /// For typical states with 5-15 facts, this is 3-4 comparisons.
        /// </summary>
        public bool HasFact(int id) => ActiveIds.BinarySearch(id) >= 0;
    }

    private Buffer _buffer;

    public State()
    {
        int capacity = Math.Max(FactRegistry.Count + 10, 64); 
        _buffer = new Buffer(capacity);
    }
    
    private State(Buffer buffer) => _buffer = buffer;

    public static State Empty() => new State();

    /// <summary>
    /// Resets all facts to false/unset.
    /// </summary>
    public void Clear()
    {
        EnsureUnique();
        _buffer.ActiveIds.Clear();
        _buffer.CachedHash = null;
    }

    public void Set(string name, FactValue value)
    {
        int id = FactRegistry.GetId(name);
        Set(id, value); // Delegate to id-based overload to maintain ActiveIds
    }

    public void Set(int id, FactValue value)
    {
        EnsureUnique();
        EnsureCapacity(id);
        
        // Track new active IDs - only add if not already present (sorted insert)
        int insertIdx = _buffer.ActiveIds.BinarySearch(id);
        if (insertIdx < 0)
        {
            _buffer.ActiveIds.Insert(~insertIdx, id);
        }
        
        _buffer.Facts[id] = value;
        _buffer.CachedHash = null;
    }

    public bool TryGet(string name, out FactValue value)
    {
        int id = FactRegistry.GetId(name);
        return TryGet(id, out value);
    }

    public bool TryGet(int id, out FactValue value)
    {
        if (id < _buffer.Facts.Length && _buffer.HasFact(id))
        {
            value = _buffer.Facts[id];
            return true;
        }
        value = default;
        return false;
    }

    public bool Satisfies(State goal)
    {
        // O(n * log m) where n = goal's active facts, m = our active facts
        var goalActiveIds = goal._buffer.ActiveIds;
        for (int j = 0; j < goalActiveIds.Count; j++)
        {
            int i = goalActiveIds[j];
            
            if (!_buffer.HasFact(i))
            {
                return false;
            }
            
            var myVal = _buffer.Facts[i];
            var goalVal = goal._buffer.Facts[i];
            
            if (myVal.Equals(goalVal)) continue;

            // Integer facts are treated as "Resources": Goal is Minimum Required
            if (myVal.Type == FactType.Int && goalVal.Type == FactType.Int)
            {
                if (myVal.IntValue < goalVal.IntValue) return false;
                continue;
            }

            return false;
        }
        return true;
    }
    
    public State Clone()
    {
        _buffer.IsShared = true;
        return new State(_buffer);
    }
    
    /// <summary>
    /// Creates a deep copy that's ready for immediate mutation.
    /// Use this instead of Clone() when you know you'll modify the result.
    /// Avoids COW overhead on first Set() call.
    /// </summary>
    public State CloneMutable()
    {
        return new State(_buffer.CloneDeep());
    }

    public int GetDeterministicHash()
    {
        if (_buffer.CachedHash.HasValue)
            return _buffer.CachedHash.Value;

        // Deterministic: ActiveIds is kept sorted, so iteration order is consistent.
        // O(n) where n = active facts, instead of O(capacity)
        int hash = 17;
        var activeIds = _buffer.ActiveIds;
        for (int j = 0; j < activeIds.Count; j++)
        {
            int i = activeIds[j];
            var fact = _buffer.Facts[i];
            hash = HashCode.Combine(hash, i, fact.IntValue, fact.Type);
        }

        _buffer.CachedHash = hash;
        return hash;
    }

    public IEnumerable<KeyValuePair<string, FactValue>> Facts => EnumerateFacts();
    
    public FactIdEnumerable FactsById =>
        new FactIdEnumerable(_buffer.Facts, _buffer.ActiveIds);

    public IEnumerator<KeyValuePair<string, FactValue>> GetEnumerator()
    {
        foreach (var fact in EnumerateFacts())
        {
            yield return fact;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<KeyValuePair<string, FactValue>> EnumerateFacts()
    {
        // O(n) where n = active facts
        var activeIds = _buffer.ActiveIds;
        for (int j = 0; j < activeIds.Count; j++)
        {
            int id = activeIds[j];
            yield return new KeyValuePair<string, FactValue>(FactRegistry.GetName(id), _buffer.Facts[id]);
        }
    }

    /// <summary>
    /// Allocation-free enumerable over fact ids -> values.
    /// Now O(n) where n = active facts, instead of O(capacity).
    /// </summary>
    public readonly struct FactIdEnumerable : IEnumerable<KeyValuePair<int, FactValue>>
    {
        private readonly FactValue[] _facts;
        private readonly List<int> _activeIds;

        public FactIdEnumerable(FactValue[] facts, List<int> activeIds)
        {
            _facts = facts;
            _activeIds = activeIds;
        }

        public FactIdEnumerator GetEnumerator() => new FactIdEnumerator(_facts, _activeIds);

        IEnumerator<KeyValuePair<int, FactValue>> IEnumerable<KeyValuePair<int, FactValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Fast enumerator that directly iterates the active ID list.
    /// MoveNext is now O(1) per call instead of O(capacity/n) average.
    /// </summary>
    public struct FactIdEnumerator : IEnumerator<KeyValuePair<int, FactValue>>
    {
        private readonly FactValue[] _facts;
        private readonly List<int> _activeIds;
        private int _index;

        public KeyValuePair<int, FactValue> Current { get; private set; }
        object IEnumerator.Current => Current;

        public FactIdEnumerator(FactValue[] facts, List<int> activeIds)
        {
            _facts = facts;
            _activeIds = activeIds;
            _index = -1;
            Current = default;
        }

        public bool MoveNext()
        {
            if (++_index < _activeIds.Count)
            {
                int id = _activeIds[_index];
                Current = new KeyValuePair<int, FactValue>(id, _facts[id]);
                return true;
            }
            return false;
        }

        public void Reset() => _index = -1;
        public void Dispose() { }
    }

    private void EnsureCapacity(int id)
    {
        if (id >= _buffer.Facts.Length)
        {
            int newSize = Math.Max(id + 1, _buffer.Facts.Length * 2);
            Array.Resize(ref _buffer.Facts, newSize);
        }
    }

    /// <summary>
    /// Ensure this state has a unique buffer before mutation (copy-on-write).
    /// </summary>
    private void EnsureUnique()
    {
        if (_buffer.IsShared)
        {
            _buffer = _buffer.CloneDeep();
        }
    }
    
    public override string ToString()
    {
        // O(n) where n = active facts
        var s = "State: ";
        var activeIds = _buffer.ActiveIds;
        for (int j = 0; j < activeIds.Count; j++)
        {
            int i = activeIds[j];
            var val = _buffer.Facts[i].Type == FactType.Int ? _buffer.Facts[i].IntValue.ToString() : _buffer.Facts[i].BoolValue.ToString();
            s += $"{FactRegistry.GetName(i)}={val}|";
        }
        return s;
    }
}
