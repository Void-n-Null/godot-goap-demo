using Godot;
using Game.Data;
using Game.Data.Components;

namespace Game.Data;

/// <summary>
/// 2D entity with position and transform data.
/// </summary>
public class Entity2D : Entity
{
    /// <summary>
    /// Position component (automatically added).
    /// </summary>
    public TransformComponent2D Transform => GetComponent<TransformComponent2D>();

    public Entity2D(Vector2 position = default)
    {
        AddComponent(new TransformComponent2D(position));
    }
}