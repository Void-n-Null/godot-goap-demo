using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Data;

internal static class TagRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, int> NameToId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> IdToName = new() { string.Empty }; // index 0 unused
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
            if (id == IdToName.Count) IdToName.Add(name); else IdToName[id] = name;
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
        lock (Sync)
        {
            return (id > 0 && id < IdToName.Count) ? IdToName[id] : string.Empty;
        }
    }

    private static void EnsureCapacity(int id)
    {
        while (IdToName.Count <= id)
        {
            IdToName.Add(string.Empty);
        }
    }
}


