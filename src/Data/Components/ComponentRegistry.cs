using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Data.Components;

/// <summary>
/// Assigns unique integer IDs to component types for array-based storage.
/// </summary>
public static class ComponentRegistry
{
    private static int _nextId = 0;
    private static readonly Dictionary<Type, int> _ids = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the unique ID for a component type.
    /// Thread-safe.
    /// </summary>
    public static int GetId(Type type)
    {
        if (_ids.TryGetValue(type, out var id))
        {
            return id;
        }

        lock (_lock)
        {
            if (!_ids.TryGetValue(type, out id))
            {
                id = _nextId++;
                _ids[type] = id;
            }
            return id;
        }
    }
}

/// <summary>
/// Generic cache for component IDs.
/// Accessing ComponentType<T>.Id is extremely fast (static field access).
/// </summary>
/// <typeparam name="T"></typeparam>
public static class ComponentType<T>
{
    public static readonly int Id = ComponentRegistry.GetId(typeof(T));
    
    /// <summary>
    /// True if T is a sealed class.
    /// If sealed, we can skip polymorphic searches if the primary slot is empty.
    /// </summary>
    public static readonly bool IsSealed = typeof(T).IsSealed;
}
