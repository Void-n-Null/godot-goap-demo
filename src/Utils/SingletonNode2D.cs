using Godot;
using System;
using System.Collections.Generic;

namespace Game.Utils;

/// <summary>
/// Base class for singleton Node2D implementations.
/// Optimized for performance with lazy initialization and efficient scene tree search.
/// </summary>
public partial class SingletonNode2D<T> : Node2D where T : SingletonNode2D<T>
{
    private static T _instance;
    private static readonly object _lock = new();
    private static bool _isInitialized;

    /// <summary>
    /// Gets the singleton instance of type T.
    /// Thread-safe lazy initialization with optimized scene tree search.
    /// </summary>
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        InitializeInstance();
                    }
                }
            }
            return _instance;
        }
    }

    private static void InitializeInstance()
    {
        // First try to get from current scene tree
        if (Engine.GetMainLoop() is SceneTree sceneTree && sceneTree.Root != null)
        {
            _instance = FindInstanceInSceneTree(sceneTree.Root);
        }

        // If not found and not yet initialized, this is an error
        if (_instance == null && !_isInitialized)
        {
            throw new InvalidOperationException(
                $"Singleton instance of {typeof(T).Name} not found. " +
                "Ensure the singleton node is added to the scene tree before accessing Instance.");
        }
    }

    /// <summary>
    /// Optimized breadth-first search to prevent stack overflow on deep hierarchies.
    /// </summary>
    private static T FindInstanceInSceneTree(Node root)
    {
        var queue = new Queue<Node>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            // Check current node
            if (current is T instance)
            {
                return instance;
            }

            // Add children to queue (breadth-first)
            foreach (Node child in current.GetChildren())
            {
                queue.Enqueue(child);
            }
        }

        return null;
    }

    public override void _Ready()
    {
        base._Ready();

        lock (_lock)
        {
            if (_instance != null && _instance != this)
            {
                GD.PushWarning($"Multiple instances of {typeof(T).Name} detected. " +
                              "Using the first initialized instance.");
                return;
            }

            _instance = this as T;
            _isInitialized = true;
        }
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        // Only clear instance if this is the current instance
        lock (_lock)
        {
            if (_instance == this)
            {
                _instance = null;
                _isInitialized = false;
            }
        }
    }

    /// <summary>
    /// Checks if the singleton instance exists without triggering initialization.
    /// </summary>
    public static bool HasInstance => _instance != null;

    /// <summary>
    /// Force re-initialization of the singleton (use with caution).
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _instance = null;
            _isInitialized = false;
        }
    }
}