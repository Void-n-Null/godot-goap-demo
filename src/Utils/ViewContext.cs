using Godot;

namespace Game.Utils;

/// <summary>
/// Static context for view-related defaults (e.g., default parent for view nodes).
/// </summary>
public static class ViewContext
{
    /// <summary>
    /// Optional default parent node where visual components attach their ViewNode
    /// when no explicit ParentNode is provided.
    /// </summary>
    public static Node DefaultParent { get; set; }

    /// <summary>
    /// Optional default view scene used when a VisualComponent has no ScenePath.
    /// Should instantiate to a Node2D with an optional "Sprite" child of type Sprite2D.
    /// </summary>
    public static PackedScene DefaultViewScene { get; set; }

    public static Vector2 DefaultScale { get; set; } = new(0.2f, 0.2f);

    /// <summary>
    /// Cached global mouse position for this frame, updated by the game loop.
    /// Components may read this to avoid per-entity native calls.
    /// </summary>
    public static Vector2? CachedMouseGlobalPosition { get; set; }
}


