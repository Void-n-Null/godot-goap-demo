using System;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Game.Universe;
using Godot;

namespace Game.Data.GOAP;

public sealed class GoToAction(Guid targetEntityId) : IAction, IRuntimeGuard
{
    private readonly Guid _targetEntityId = targetEntityId;
    private NPCMotorComponent _motor;
    private Entity _targetEntity;
    private bool _arrived;
    private bool _failed;
    private Vector2 _currentTarget;

    public void Enter(State ctx)
    {
        var actor = ctx.Agent;
        if (!actor.TryGetComponent<NPCMotorComponent>(out var motor))
        {
            Fail($"NPCMotorComponent not found when starting GoToAction on {actor.Name}");
            _failed = true;
            return;
        }
        _motor = motor;
        _arrived = false;
        GD.Print($"GoToAction Enter for target {_targetEntityId}: motor found");

        _targetEntity = ctx.World.EntityManager.GetEntityById(_targetEntityId);
        if (_targetEntity == null)
        {
            Fail($"Target entity {_targetEntityId} not found when starting GoToAction on {actor.Name}");
            _failed = true;
            return;
        }
        GD.Print($"GoToAction Enter for target {_targetEntityId}: target found");

        UpdateTargetPosition();
        if (!_failed)
        {
            _motor.OnTargetReached += OnArrived;
            _motor.Target = _currentTarget;
            GD.Print($"GoToAction started movement to {_currentTarget}");
        }
    }

    private void UpdateTargetPosition()
    {
        if (_targetEntity?.TryGetComponent<TransformComponent2D>(out var transform) == true)
        {
            _currentTarget = transform.Position;
            _motor.Target = _currentTarget;
        }
        else
        {
            Fail("Target entity has no TransformComponent2D");
            _failed = true;
        }
    }

    private void OnArrived() { _arrived = true; }

    public ActionStatus Update(State ctx, float dt)
    {
        if (_failed || _targetEntity == null) 
        {
            Fail("GoToAction failed: target missing or invalid setup");
            return ActionStatus.Failed;
        }

        var currentPos = ctx.Agent.GetComponent<TransformComponent2D>().Position;
        GD.Print($"GoTo Update: arrived={_arrived}, target exists, position={currentPos}, target={_currentTarget}, distance={currentPos.DistanceTo(_currentTarget)}");

        // Optionally update target if it moved, for pursuing
        // UpdateTargetPosition();
        // if (!_failed) _motor.Target = _currentTarget;

        return _arrived ? ActionStatus.Succeeded : ActionStatus.Running;
    }

    public void Exit(State ctx, ActionExitReason reason)
    {
        if (_motor == null) return;
        _motor.OnTargetReached -= OnArrived;
        if (reason != ActionExitReason.Completed) _motor.Target = null; // stop if cancelled/failed
    }

    public bool StillValid(State ctx)
    {
        if (!_failed && _motor != null && ctx.World.EntityManager.GetEntityById(_targetEntityId) != null)
            return true;
        
        if (!_failed) Fail("GoToAction no longer valid: target destroyed or motor invalid");
        GD.Print($"GoTo StillValid: false");
        return false;
    }

    public void Fail(string reason)
    {
        GD.PushError($"GoToAction fail: {reason}");
    }
}
