using Godot;
using Game.Data.Components;
using Game.Data;
using System;


namespace Game.Data.Components;

public class NPCMotorComponent : MotorComponent
{
    public Action OnTargetReached;
    public float TargetReachedRadius = 8f;

    public NPCMotorComponent(float maxSpeed, float friction, float maxAcceleration, float? targetReachedRadius = null, Action onTargetReached = null) : base(maxSpeed, friction, maxAcceleration)
    {
        TargetReachedRadius = targetReachedRadius ?? 8f;
        OnTargetReached = onTargetReached ?? (() => { });
    }

    public override void HandleSteeringToTarget(double delta) 
    {
        //Since this method only runs when the target is set, we only have to worry about emmiting when we reach the target.
        //And setting the target to null to stop the motor.

        var vectorToTarget = Target.Value - _transform2D.Position;
        var distance = vectorToTarget.Length();
        if (distance <= TargetReachedRadius)
        {
            OnTargetReached?.Invoke();
            Target = null;
            return;
        }

        //If we haven't reached the target, we need to steer our acceleration towards the target.
        var desiredAcceleration = vectorToTarget.Normalized() * MaxAcceleration;
        Acceleration = desiredAcceleration;
    }
}