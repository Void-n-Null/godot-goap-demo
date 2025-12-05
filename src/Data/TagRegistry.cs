using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Data;

internal static class TagRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, int> NameToId = new(StringComparer.OrdinalIgnoreCase);
    // Lock-free reads via Volatile.Read on the array reference; index 0 unused.
    private static string[] IdToName = { string.Empty };
    private static int _nextId = 0; // will increment to 1 on first tag

    public static int GetOrCreateId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name must be non-empty", nameof(name));

        lock (Sync)
        {
            if (NameToId.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var id = Interlocked.Increment(ref _nextId);
            NameToId[name] = id;
            EnsureCapacity(id);
            // We are under lock; safe to write the published array.
            IdToName[id] = name;
            return id;
        }
    }

    public static bool TryGetId(string name, out int id)
    {
        if (string.IsNullOrEmpty(name))
        {
            id = 0;
            return false;
        }

        lock (Sync)
        {
            return NameToId.TryGetValue(name, out id);
        }
    }

    public static string GetName(int id)
    {
        var snapshot = Volatile.Read(ref IdToName);
        return (id > 0 && id < snapshot.Length) ? snapshot[id] : string.Empty;
    }

    private static void EnsureCapacity(int id)
    {
        if (id < IdToName.Length)
            return;

        // Double the array size until it fits; performed under lock.
        var newLength = IdToName.Length;
        while (newLength <= id)
        {
            newLength *= 2;
        }

        var expanded = new string[newLength];
        Array.Copy(IdToName, expanded, IdToName.Length);
        // Publish the new array for lock-free readers.
        IdToName = expanded;
    }
}


