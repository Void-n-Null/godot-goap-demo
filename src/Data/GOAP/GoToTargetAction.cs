using System;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Game.Universe;
using Godot;
using System.Linq;

namespace Game.Data.GOAP;

public sealed class GoToTargetAction(TargetType type) : IAction, IRuntimeGuard
{
    private readonly TargetType _type = type;
    private NPCMotorComponent _motor;
    private bool _arrived;
    private Vector2 _currentTarget;
    private Entity _targetEntity;

    public void Enter(RuntimeContext ctx)
    {
        var agent = ctx.Agent;
        if (!agent.TryGetComponent<NPCMotorComponent>(out var motor))
            throw new Exception($"NPCMotorComponent not found for GoToTargetAction on {agent.Name}");

        _motor = motor;
        _arrived = false;

        // Debug: Check what entities exist
        var allWithTarget = ctx.World.EntityManager.QueryByComponent<TargetComponent>(agent.Transform.Position, float.MaxValue);
        GD.Print($"GoToTargetAction looking for {_type}. Found {allWithTarget.Count} entities with TargetComponent near agent at {agent.Transform.Position}");
        foreach (var e in allWithTarget.Take(5))
        {
            var tc = e.GetComponent<TargetComponent>();
            var pos = e.TryGetComponent<TransformComponent2D>(out var t) ? t.Position : Vector2.Zero;
            GD.Print($"  - Entity {e.Name} has TargetType={tc.Target} at position {pos}");
        }

        _targetEntity = allWithTarget.FirstOrDefault(e => e.GetComponent<TargetComponent>().Target == _type);
        if (_targetEntity == null)
        {
            Fail($"No {_type} target available for GoToTargetAction (checked {allWithTarget.Count} entities)");
            return;
        }

        GD.Print($"GoToTargetAction found target: {_targetEntity.Name} at {_targetEntity.Transform.Position}");
        UpdateTargetPosition();
        _motor.OnTargetReached += OnArrived;
        _motor.Target = _currentTarget;
    }

    private void UpdateTargetPosition()
    {
        if (_targetEntity?.TryGetComponent<TransformComponent2D>(out var transform) == true)
        {
            _currentTarget = transform.Position;
        }
        else
        {
            Fail("Target has no TransformComponent2D");
        }
    }

    private void OnArrived() { _arrived = true; }

    public ActionStatus Update(RuntimeContext ctx, float dt)
    {
        if (_targetEntity == null) 
        {
            Fail("GoToTargetAction failed: target missing");
            return ActionStatus.Failed;
        }

        return _arrived ? ActionStatus.Succeeded : ActionStatus.Running;
    }

    public void Exit(RuntimeContext ctx, ActionExitReason reason)
    {
        if (_motor == null) return;
        _motor.OnTargetReached -= OnArrived;
        if (reason != ActionExitReason.Completed) _motor.Target = null;
    }

    public bool StillValid(RuntimeContext ctx)
    {
        var anyAvailable = ctx.World.EntityManager.QueryByComponent<TargetComponent>(Vector2.Zero, float.MaxValue)
            .Any(e => e.GetComponent<TargetComponent>().Target == _type);
        return anyAvailable;
    }

    public void Fail(string reason)
    {
        GD.PushError($"GoToTargetAction fail: {reason}");
    }
}
