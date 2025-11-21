using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Game.Data.GOAP;

public class State : IEnumerable<KeyValuePair<string, FactValue>>
{
    private FactValue[] _facts; 
    private System.Collections.BitArray _mask; 
    private int? _cachedHash;

    public State()
    {
        int capacity = Math.Max(FactRegistry.Count + 10, 64); 
        _facts = new FactValue[capacity];
        _mask = new System.Collections.BitArray(capacity);
    }
    
    private State(FactValue[] facts, System.Collections.BitArray mask)
    {
        _facts = facts;
        _mask = mask;
    }

    public static State Empty() => new State();

    /// <summary>
    /// Resets all facts to false/unset.
    /// </summary>
    public void Clear()
    {
        _mask.SetAll(false);
        _cachedHash = null;
    }

    public void Set(string name, FactValue value)
    {
        int id = FactRegistry.GetId(name);
        EnsureCapacity(id);
        _facts[id] = value;
        _mask.Set(id, true);
        _cachedHash = null;
    }

    public void Set(int id, FactValue value)
    {
        EnsureCapacity(id);
        _facts[id] = value;
        _mask.Set(id, true);
        _cachedHash = null;
    }

    public bool TryGet(string name, out FactValue value)
    {
        int id = FactRegistry.GetId(name);
        return TryGet(id, out value);
    }

    public bool TryGet(int id, out FactValue value)
    {
        if (id < _mask.Length && _mask.Get(id))
        {
            value = _facts[id];
            return true;
        }
        value = default;
        return false;
    }

    public bool Satisfies(State goal)
    {
        for (int i = 0; i < goal._mask.Length; i++)
        {
            if (goal._mask.Get(i))
            {
                 if (i >= _mask.Length || !_mask.Get(i))
                 {
                     return false;
                 }
                 
                 var myVal = _facts[i];
                 var goalVal = goal._facts[i];
                 
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
        var newFacts = new FactValue[_facts.Length];
        Array.Copy(_facts, newFacts, _facts.Length);
        return new State(newFacts, (System.Collections.BitArray)_mask.Clone());
    }

    public int GetDeterministicHash()
    {
        if (_cachedHash.HasValue)
            return _cachedHash.Value;

        var indices = new List<int>();
        for (int i = 0; i < _mask.Length; i++)
        {
            if (_mask.Get(i))
            {
                indices.Add(i);
            }
        }
        indices.Sort();

        int hash = 17;
        foreach (var id in indices)
        {
            var fact = _facts[id];
            hash = HashCode.Combine(hash, id, fact.IntValue, fact.Type);
        }

        _cachedHash = hash;
        return hash;
    }

    public IEnumerable<KeyValuePair<string, FactValue>> Facts => EnumerateFacts();

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
        for (int id = 0; id < _mask.Length && id < maxId; id++)
        {
            if (_mask.Get(id))
            {
                yield return new KeyValuePair<string, FactValue>(FactRegistry.GetName(id), _facts[id]);
            }
        }
    }

    private void EnsureCapacity(int id)
    {
        if (id >= _facts.Length)
        {
            int newSize = Math.Max(id + 1, _facts.Length * 2);
            Array.Resize(ref _facts, newSize);
            var newMask = new System.Collections.BitArray(newSize);
            for(int i=0; i<_mask.Length; i++) newMask[i] = _mask[i];
            _mask = newMask;
        }
    }
    
    public override string ToString()
    {
        var s = "State: ";
        for(int i=0; i<_mask.Length; i++)
        {
            if(_mask.Get(i))
            {
                var val = _facts[i].Type == FactType.Int ? _facts[i].IntValue.ToString() : _facts[i].BoolValue.ToString();
                s += $"{FactRegistry.GetName(i)}={val}|";
            }
        }
        return s;
    }
}
