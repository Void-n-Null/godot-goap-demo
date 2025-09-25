using Game.Data.Components;
using Godot;
using System;

namespace Game.Data.NPCActions;

public sealed class MoveTo(Vector2 target) : IAction, IRuntimeGuard
{
    private readonly Vector2 _target = target;
    private NPCMotorComponent _motor;
    private bool _arrived;

    public void Enter(Entity actor)
    {
        if (!actor.TryGetComponent<NPCMotorComponent>(out var motor)) 
            throw new Exception($"NPCMotorComponent not found when starting MoveTo on {actor.Name}");
        _motor = motor;
        _arrived = false;
        _motor.OnTargetReached += OnArrived;
        _motor.Target = _target;
    }

    void OnArrived() { _arrived = true; }

    public ActionStatus Update(Entity actor, float dt) => _arrived ? ActionStatus.Succeeded : ActionStatus.Running;

    public void Exit(Entity actor, ActionExitReason reason)
    {
        if (_motor == null) return;
        _motor.OnTargetReached -= OnArrived;
        if (reason != ActionExitReason.Completed) _motor.Target = null; // stop if cancelled/failed
    }

    // Optional guard: fail if path becomes impossible, target deleted, etc.
    public bool StillValid(Entity actor)
        => _motor != null /* && _motor.HasPath */; // add real checks as needed
}
