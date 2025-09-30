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

        // Find unreserved targets only
        var allWithTarget = ctx.World.EntityManager.QueryByComponent<TargetComponent>(agent.Transform.Position, float.MaxValue);
        var availableTargets = allWithTarget
            .Where(e => e.GetComponent<TargetComponent>().Target == _type)
            .Where(e => ResourceReservationManager.Instance.IsAvailableFor(e, agent))
            .OrderBy(e => agent.Transform.Position.DistanceTo(e.Transform.Position))
            .ToList();

        if (availableTargets.Count == 0)
        {
            GD.Print($"[{agent.Name}] GoToTargetAction: No unreserved {_type} available (total {_type}s: {allWithTarget.Count(e => e.GetComponent<TargetComponent>().Target == _type)})");
            Fail($"No unreserved {_type} target available");
            return;
        }

        _targetEntity = availableTargets.First();
        
        // Reserve the target
        if (!ResourceReservationManager.Instance.TryReserve(_targetEntity, agent))
        {
            Fail($"Failed to reserve {_type} target");
            return;
        }

        GD.Print($"[{agent.Name}] GoToTargetAction: Going to {_targetEntity.Name} at {_targetEntity.Transform.Position}");
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
        if (reason != ActionExitReason.Completed)
        {
            _motor.Target = null;
            // Release reservation if we didn't complete (completed = ChopAction will use it)
            if (_targetEntity != null)
            {
                ResourceReservationManager.Instance.Release(_targetEntity, ctx.Agent);
            }
        }
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
