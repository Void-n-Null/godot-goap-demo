using Godot;
using Game.Data;
using Game.Data.Components;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Movement component with velocity and acceleration.
/// </summary>
public class MovementComponent(float MaxSpeed = 500f, float Friction = 0.9f) : IActiveComponent
{
    public Vector2 Velocity { get; set; }
    public Vector2 Acceleration { get; set; }
    public float MaxSpeed { get; set; } = MaxSpeed;
    public float Friction { get; set; } = Friction;

    // Cached position component reference (set in PostAttached)
    private TransformComponent2D _transform2D;

    public Entity Entity { get; set; }

    public void Update(double delta)
    {
        if (_transform2D == null) return;

        // Move at constant speed toward cached mouse cursor (global coordinates)
        var mouse = ViewContext.CachedMouseGlobalPosition;
        if (mouse.HasValue)
        {
            var toMouse = mouse.Value - _transform2D.Position;
            if (toMouse.Length() > 0.001f)
            {
                var direction = toMouse.Normalized();
                Velocity = direction * MaxSpeed; // constant-speed movement
                _transform2D.Position += Velocity * (float)delta;
            }
            else
            {
                Velocity = Vector2.Zero;
            }
        }
    }

    public void OnPreAttached()
    {
        // Phase 1: Component is attached, but don't access other components yet
        // Could initialize movement-related properties here
    }

    public void OnPostAttached()
    {
        // Phase 2: All components attached, safe to get position component
        _transform2D = Entity.GetComponent<TransformComponent2D>();

        if (_transform2D == null)
        {
            GD.PushWarning($"MovementComponent: No Transform2D found on entity {Entity.Id}. Movement disabled.");
            // Component effectively shuts down - Update() will do nothing
        }
    }

    public void OnDetached()
    {
        _transform2D = null;
        Velocity = Vector2.Zero;
        Acceleration = Vector2.Zero;
    }
}
