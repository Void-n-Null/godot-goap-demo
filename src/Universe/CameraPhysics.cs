using Godot;

namespace Game.Universe;

/// <summary>
/// Standalone camera physics helper for momentum and damping.
/// Encapsulates critically-damped spring toward a target and exponential friction fallback.
/// </summary>
public class CameraPhysics
{
    private const float EpsilonSpeedSq = 0.000001f;

    public float InertiaFrequency { get; set; } = 6.0f; // 1/s
    public float InertiaTravelSeconds { get; set; } = 0.35f;
    public float InertiaStopDistance { get; set; } = 0.5f;
    public float InertiaStopSpeed { get; set; } = 5.0f;
    public float PanFriction { get; set; } = 6.0f; // 1/s
    public float MaxPanSpeed { get; set; } = 50000.0f;

    // Runtime state
    public bool InertiaActive { get; private set; }
    public Vector2 InertiaTarget { get; private set; }

    public void CancelInertia()
    {
        InertiaActive = false;
    }

    public void StartInertia(Vector2 currentPosition, Vector2 currentVelocity)
    {
        if (currentVelocity.LengthSquared() <= EpsilonSpeedSq || InertiaTravelSeconds <= 0.0f)
        {
            InertiaActive = false;
            return;
        }
        InertiaTarget = currentPosition + currentVelocity * InertiaTravelSeconds;
        InertiaActive = true;
    }

    /// <summary>
    /// Shift the inertia target by the given world delta (e.g. when camera is re-anchored during zoom).
    /// </summary>
    public void ShiftInertiaTarget(Vector2 delta)
    {
        if (!InertiaActive) return;
        InertiaTarget += delta;
    }

    /// <summary>
    /// Convenience: trigger inertia when a drag ended and velocity warrants it.
    /// </summary>
    public void MaybeStartInertia(bool endedDragging, bool shouldResetInertia, Vector2 currentPosition, Vector2 currentVelocity)
    {
        if (!endedDragging)
            return;
        if (shouldResetInertia)
            StartInertia(currentPosition, currentVelocity);
    }

    /// <summary>
    /// Step critically-damped spring toward target. Returns the new position and velocity.
    /// </summary>
    public (Vector2 position, Vector2 velocity) StepSpring(Vector2 position, Vector2 velocity, float dt)
    {
        float omega = InertiaFrequency;
        float k = omega * omega;
        float c = 2.0f * omega; // critical damping

        var toTarget = InertiaTarget - position;
        velocity += toTarget * k * dt - velocity * c * dt;

        if (velocity.Length() > MaxPanSpeed)
            velocity = velocity.Normalized() * MaxPanSpeed;

        position += velocity * dt;

        if (toTarget.Length() <= InertiaStopDistance && velocity.Length() <= InertiaStopSpeed)
        {
            position = InertiaTarget;
            velocity = Vector2.Zero;
            InertiaActive = false;
        }

        return (position, velocity);
    }

    /// <summary>
    /// Step exponential friction decay. Returns the new position and velocity.
    /// </summary>
    public (Vector2 position, Vector2 velocity) StepFriction(Vector2 position, Vector2 velocity, float dt)
    {
        position += velocity * dt;
        float decay = Mathf.Exp(-PanFriction * dt);
        velocity *= decay;
        if (velocity.LengthSquared() < EpsilonSpeedSq)
            velocity = Vector2.Zero;
        return (position, velocity);
    }

    /// <summary>
    /// Apply momentum for this frame. If dragging, no momentum is applied.
    /// Otherwise, uses spring if active, else friction.
    /// </summary>
    public (Vector2 position, Vector2 velocity) StepMomentum(bool isDragging, Vector2 position, Vector2 velocity, float dt)
    {
        if (isDragging)
            return (position, velocity);

        if (InertiaActive && InertiaFrequency > 0.0f && InertiaTravelSeconds > 0.0f)
            return StepSpring(position, velocity, dt);

        if (velocity.LengthSquared() > EpsilonSpeedSq)
            return StepFriction(position, velocity, dt);

        return (position, velocity);
    }
}


