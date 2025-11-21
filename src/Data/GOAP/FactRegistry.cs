using System.Collections.Generic;

namespace Game.Data.GOAP;

// This class ensures "IsHungry" always maps to ID 5 (example)
public static class FactRegistry
{
    private static readonly Dictionary<string, int> _nameToId = new();
    private static readonly List<string> _idToName = new();

    public static int GetId(string name)
    {
        if (_nameToId.TryGetValue(name, out int id))
            return id;

        // It's a new fact, register it
        id = _idToName.Count;
        _nameToId[name] = id;
        _idToName.Add(name);
        return id;
    }

    public static string GetName(int id) => _idToName[id];
    public static int Count => _idToName.Count;
}
