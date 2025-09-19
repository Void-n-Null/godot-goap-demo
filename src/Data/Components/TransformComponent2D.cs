using Godot;
using Game.Data;
using Game.Data.Components;
using System;
namespace Game.Data.Components;

/// <summary>
/// Position and transform data.
/// </summary>
[Flags]
public enum TransformDirtyFlags
{
    None = 0,
    Position = 1 << 0,
    Rotation = 1 << 1,
    Scale = 1 << 2,
    All = Position | Rotation | Scale,
}

public class TransformComponent2D(Vector2 Position = default, float Rotation = default, Vector2? Scale = null) : IComponent
{
    private Vector2 _position = Position;
    private float _rotation = Rotation;
    private Vector2 _scale = Scale ?? Vector2.One;

    public TransformDirtyFlags DirtyMask { get; private set; } = TransformDirtyFlags.All;

    public Vector2 Position
    {
        get => _position;
        set
        {
            if (_position == value) return;
            _position = value;
            DirtyMask |= TransformDirtyFlags.Position;
        }
    }

    public float Rotation
    {
        get => _rotation;
        set
        {
            if (Mathf.IsEqualApprox(_rotation, value)) return;
            _rotation = value;
            DirtyMask |= TransformDirtyFlags.Rotation;
        }
    }

    public Vector2 Scale
    {
        get => _scale;
        set
        {
            if (_scale == value) return;
            _scale = value;
            DirtyMask |= TransformDirtyFlags.Scale;
        }
    }

    public void ClearDirty(TransformDirtyFlags mask = TransformDirtyFlags.All)
    {
        DirtyMask &= ~mask;
    }
    public Entity Entity { get; set; }
}
