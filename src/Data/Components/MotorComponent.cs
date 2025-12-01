using Godot;
using Game.Data;
using Game.Data.Components;
using Game.Utils;
using System.Collections.Generic;
using System;

namespace Game.Data.Components;

/// <summary>
/// Motor component with velocity, acceleration, damping and clamped integration.
/// Target-seeking is intentionally left to descendants.
/// </summary>
public class MotorComponent(
    float MaxSpeed = 500f,
    float Friction = 0.9f,
    float MaxAcceleration = 1500f
) : IActiveComponent
{
    public bool DebugDrawEnabled = false;
    public float DebugVectorScale = 0.2f; // scales arrow length for readability
    public Vector2 Velocity { get; set; }
    public Vector2 Acceleration { get; set; }
    public float MaxSpeed { get; set; } = MaxSpeed;
    public float Friction { get; set; } = Friction;
    public float MaxAcceleration { get; set; } = MaxAcceleration;
    public Vector2? Target { get; set; }
    // Cached position component reference (set in PostAttached)
    protected TransformComponent2D _transform2D;

    

    public Entity Entity { get; set; }

    public void Update(double delta)
    {
        if (_transform2D == null)
            return;
        
        // 1) Compute steering BEFORE integration so new acceleration applies this frame
        if (Target != null)
        {
            HandleSteeringToTarget(delta);
        }
        else
        {
            // If there is no target, clear any previously set acceleration so we actually idle
            Acceleration = Vector2.Zero;
        }

        // 2) Apply external acceleration input (if any), limited by MaxAcceleration
        if (Acceleration != Vector2.Zero)
        {
            var cappedAcceleration = Acceleration.LimitLength(MaxAcceleration);
            Velocity += cappedAcceleration * (float)delta;
        }

        // 3) Apply velocity damping (friction) — interpreted as per-second coefficient in [0,1)
        if (Friction > 0f && Friction < 1f)
        {   
            var dampingFactor = Mathf.Pow(Friction, (float)delta);
            Velocity *= dampingFactor;
        }

        // 4) Clamp to max speed and integrate
        Velocity = Velocity.LimitLength(MaxSpeed);
        _transform2D.Position += Velocity * (float)delta;

        // 5) Optional debug draw for velocity (yellow) and acceleration (blue)
        if (DebugDrawEnabled && Utils.EntityRendererFinder.Renderer != null)
        {
            var origin = _transform2D.Position;
            var velVec = Velocity * DebugVectorScale;
            var accVec = Acceleration * DebugVectorScale;
            Utils.EntityRendererFinder.Renderer.QueueDebugVector(origin, velVec, Colors.Yellow, 2f);
            if (Acceleration != Vector2.Zero)
                Utils.EntityRendererFinder.Renderer.QueueDebugVector(origin, accVec, Colors.DodgerBlue, 2f);
        }
    }


    public virtual void HandleSteeringToTarget(double delta){}

    public void OnPostAttached()
    {
        // Phase 2: All components attached, safe to get position component
        _transform2D = Entity.GetComponent<TransformComponent2D>();
    }
	public IEnumerable<ComponentDependency> GetRequiredComponents()
	{
		yield return ComponentDependency.Of<TransformComponent2D>();
	}

    public void OnDetached()
    {
        _transform2D = null;
        Velocity = Vector2.Zero;
        Acceleration = Vector2.Zero;
    }
}
