using Godot;
using Game.Data;
using Game.Data.Components;

namespace Game.Data;

/// <summary>
/// Visual entity that creates and manages a Node2D view.
/// </summary>
public class VisualEntity2D : Entity2D
{
    /// <summary>
    /// Visual component (automatically added).
    /// </summary>
    public VisualComponent Visual => GetComponent<VisualComponent>();

    public VisualEntity2D(Vector2 position = default, string scenePath = null) : base(position)
    {
        AddComponent(new VisualComponent(scenePath));
    }
}