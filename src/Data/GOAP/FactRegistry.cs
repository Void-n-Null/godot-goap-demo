using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Game.Data.GOAP;

// This class ensures "IsHungry" always maps to ID 5 (example)
public static class FactRegistry
{
    private static readonly ConcurrentDictionary<string, int> _nameToId = new();
    private static readonly List<string> _idToName = new();
    private static readonly object _lock = new();
    private static volatile int _count = 0;

    public static int GetId(string name)
    {
        // Fast path: lock-free read
        if (_nameToId.TryGetValue(name, out int id))
            return id;

        lock (_lock)
        {
            // Double-check locking
            if (_nameToId.TryGetValue(name, out id))
                return id;

            // It's a new fact, register it
            id = _idToName.Count;
            // Add to list first
            _idToName.Add(name);
            // Then to dictionary (ConcurrentDictionary is safe)
            _nameToId[name] = id;
            
            // Update volatile count last to ensure readers see consistent state
            _count = _idToName.Count;
            
            return id;
        }
    }

    public static string GetName(int id)
    {
        // We still need a lock here because List is not thread-safe for reads while writes are happening
        // However, writes only happen when a new fact is discovered. 
        // If we assume stable set of facts after initialization, we could optimize this too, 
        // but GetId is the main hot path.
        lock (_lock)
        {
            return _idToName[id];
        }
    }

    public static int Count => _count;
}
