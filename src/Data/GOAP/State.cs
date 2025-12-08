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
        public System.Collections.BitArray Mask;
        public int? CachedHash;
        public bool IsShared;

        public Buffer(int capacity)
        {
            Facts = new FactValue[capacity];
            Mask = new System.Collections.BitArray(capacity);
            CachedHash = null;
            IsShared = false;
        }

        private Buffer(FactValue[] facts, System.Collections.BitArray mask)
        {
            Facts = facts;
            Mask = mask;
            CachedHash = null;
            IsShared = false;
        }

        public Buffer CloneDeep()
        {
            var factsCopy = new FactValue[Facts.Length];
            Array.Copy(Facts, factsCopy, Facts.Length);
            return new Buffer(factsCopy, (System.Collections.BitArray)Mask.Clone());
        }
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
        _buffer.Mask.SetAll(false);
        _buffer.CachedHash = null;
    }

    public void Set(string name, FactValue value)
    {
        int id = FactRegistry.GetId(name);
        EnsureUnique();
        EnsureCapacity(id);
        _buffer.Facts[id] = value;
        _buffer.Mask.Set(id, true);
        _buffer.CachedHash = null;
    }

    public void Set(int id, FactValue value)
    {
        EnsureUnique();
        EnsureCapacity(id);
        _buffer.Facts[id] = value;
        _buffer.Mask.Set(id, true);
        _buffer.CachedHash = null;
    }

    public bool TryGet(string name, out FactValue value)
    {
        int id = FactRegistry.GetId(name);
        return TryGet(id, out value);
    }

    public bool TryGet(int id, out FactValue value)
    {
        if (id < _buffer.Mask.Length && _buffer.Mask.Get(id))
        {
            value = _buffer.Facts[id];
            return true;
        }
        value = default;
        return false;
    }

    public bool Satisfies(State goal)
    {
        for (int i = 0; i < goal._buffer.Mask.Length; i++)
        {
            if (goal._buffer.Mask.Get(i))
            {
                 if (i >= _buffer.Mask.Length || !_buffer.Mask.Get(i))
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
        }
        return true;
    }
    
    public State Clone()
    {
        _buffer.IsShared = true;
        return new State(_buffer);
    }

    public int GetDeterministicHash()
    {
        if (_buffer.CachedHash.HasValue)
            return _buffer.CachedHash.Value;

        // Deterministic: iterate ids in ascending order; no allocations.
        int hash = 17;
        for (int i = 0; i < _buffer.Mask.Length; i++)
        {
            if (_buffer.Mask.Get(i))
            {
                var fact = _buffer.Facts[i];
                hash = HashCode.Combine(hash, i, fact.IntValue, fact.Type);
            }
        }

        _buffer.CachedHash = hash;
        return hash;
    }

    public IEnumerable<KeyValuePair<string, FactValue>> Facts => EnumerateFacts();
    
    public FactIdEnumerable FactsById =>
        new FactIdEnumerable(
            _buffer.Facts,
            _buffer.Mask,
            Math.Min(_buffer.Mask.Length, FactRegistry.Count));

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
        int maxId = FactRegistry.Count;
        for (int id = 0; id < _buffer.Mask.Length && id < maxId; id++)
        {
            if (_buffer.Mask.Get(id))
            {
                yield return new KeyValuePair<string, FactValue>(FactRegistry.GetName(id), _buffer.Facts[id]);
            }
        }
    }

    /// <summary>
    /// Allocation-free enumerable over fact ids -> values to avoid iterator state machines.
    /// </summary>
    public readonly struct FactIdEnumerable : IEnumerable<KeyValuePair<int, FactValue>>
    {
        private readonly FactValue[] _facts;
        private readonly System.Collections.BitArray _mask;
        private readonly int _limit;

        public FactIdEnumerable(FactValue[] facts, System.Collections.BitArray mask, int limit)
        {
            _facts = facts;
            _mask = mask;
            _limit = limit;
        }

        public FactIdEnumerator GetEnumerator() => new FactIdEnumerator(_facts, _mask, _limit);

        IEnumerator<KeyValuePair<int, FactValue>> IEnumerable<KeyValuePair<int, FactValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct FactIdEnumerator : IEnumerator<KeyValuePair<int, FactValue>>
    {
        private readonly FactValue[] _facts;
        private readonly System.Collections.BitArray _mask;
        private readonly int _limit;
        private int _index;

        public KeyValuePair<int, FactValue> Current { get; private set; }
        object IEnumerator.Current => Current;

        public FactIdEnumerator(FactValue[] facts, System.Collections.BitArray mask, int limit)
        {
            _facts = facts;
            _mask = mask;
            _limit = limit;
            _index = -1;
            Current = default;
        }

        public bool MoveNext()
        {
            while (++_index < _limit)
            {
                if (_mask.Get(_index))
                {
                    Current = new KeyValuePair<int, FactValue>(_index, _facts[_index]);
                    return true;
                }
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
            var newMask = new System.Collections.BitArray(newSize);
            for(int i=0; i<_buffer.Mask.Length; i++) newMask[i] = _buffer.Mask[i];
            _buffer.Mask = newMask;
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
        var s = "State: ";
        for(int i=0; i<_buffer.Mask.Length; i++)
        {
            if(_buffer.Mask.Get(i))
            {
                var val = _buffer.Facts[i].Type == FactType.Int ? _buffer.Facts[i].IntValue.ToString() : _buffer.Facts[i].BoolValue.ToString();
                s += $"{FactRegistry.GetName(i)}={val}|";
            }
        }
        return s;
    }
}
