using Godot;
using Game.Data;
using Game.Data.Components;

namespace Game.Data.Components;

/// <summary>
/// Position and transform data.
/// </summary>
public class TransformComponent2D(Vector2 Position = default, float Rotation = default, Vector2 Scale = default) : IComponent
{
    public Vector2 Position { get; set; } = Position;
    public float Rotation { get; set; } = Rotation;
    public Vector2 Scale { get; set; } = Scale;
    public Entity Entity { get; set; }
}
