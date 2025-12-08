using System.Collections.Concurrent;
using System.Threading;

namespace Game.Data.GOAP;

/// <summary>
/// Thread-safe bidirectional mapping between fact names and integer IDs.
/// Uses lock-free reads for maximum parallel throughput.
/// </summary>
public static class FactRegistry
{
    private static readonly ConcurrentDictionary<string, int> _nameToId = new();
    
    /// <summary>
    /// Copy-on-write array for lock-free reads. Replaced atomically on writes.
    /// </summary>
    private static volatile string[] _idToName = new string[64];
    
    private static readonly object _writeLock = new();
    private static volatile int _count = 0;

    public static int GetId(string name)
    {
        // Fast path: lock-free read from ConcurrentDictionary
        if (_nameToId.TryGetValue(name, out int id))
            return id;

        lock (_writeLock)
        {
            // Double-check after acquiring lock
            if (_nameToId.TryGetValue(name, out id))
                return id;

            // Register new fact
            id = _count;
            
            // Grow array if needed (copy-on-write)
            var names = _idToName;
            if (id >= names.Length)
            {
                var newNames = new string[names.Length * 2];
                names.CopyTo(newNames, 0);
                names = newNames;
            }
            
            // Write name to array
            names[id] = name;
            
            // Publish new array atomically (volatile write)
            _idToName = names;
            
            // Add to dictionary
            _nameToId[name] = id;
            
            // Update count last (volatile write ensures visibility)
            _count = id + 1;
            
            return id;
        }
    }

    /// <summary>
    /// Lock-free lookup by ID. O(1) with no contention.
    /// </summary>
    public static string GetName(int id)
    {
        // Volatile read ensures we see the latest array
        var names = _idToName;
        return names[id];
    }

    public static int Count => _count;
}
