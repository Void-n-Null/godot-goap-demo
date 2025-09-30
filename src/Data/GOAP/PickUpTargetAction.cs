using System;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Game.Universe;
using Godot;
using System.Linq;

namespace Game.Data.GOAP;

public sealed class PickUpTargetAction(TargetType type) : IAction, IRuntimeGuard
{
    private readonly TargetType _type = type;
    private Entity _nearestTarget;
    private bool _completed;
    private bool _failed;

    public void Enter(State ctx)
    {
        _nearestTarget = ctx.World.EntityManager.QueryByComponent<TargetComponent>(ctx.Agent.Transform.Position, 100f)
            .FirstOrDefault(e => e.GetComponent<TargetComponent>().Target == _type);
        if (_nearestTarget == null)
        {
            Fail($"No {_type} target nearby for PickUpTargetAction");
            _failed = true;
            return;
        }

        var targetData = _nearestTarget.GetComponent<TargetComponent>();
        var agent = ctx.Agent;
        if (!agent.TryGetComponent<NPCData>(out var npcData))
        {
            Fail("Agent lacks NPCData for inventory");
            _failed = true;
            return;
        }

        // Pick up: Add to inventory
        npcData.Resources[_type] = (npcData.Resources.TryGetValue(_type, out var current) ? current : 0) + 1;
        GD.Print($"Picked up 1 {_type}(s); total: {npcData.Resources[_type]}");

        // Remove entity from world
        ctx.World.EntityManager.UnregisterEntity(_nearestTarget);

        _completed = true;
        GD.Print($"PickUpTargetAction succeeded for {_type}");
    }

    public ActionStatus Update(State ctx, float dt)
    {
        if (_failed) return ActionStatus.Failed;
        return _completed ? ActionStatus.Succeeded : ActionStatus.Running; // Immediate on Enter
    }

    public void Exit(State ctx, ActionExitReason reason)
    {
        // No cleanup needed
    }

    public bool StillValid(State ctx)
    {
        if (_failed) return false;
        var anyAvailable = ctx.World.EntityManager.QueryByComponent<TargetComponent>(Vector2.Zero, float.MaxValue)
            .Any(e => e.GetComponent<TargetComponent>().Target == _type);
        return anyAvailable;
    }

    public void Fail(string reason)
    {
        GD.PushError($"PickUpTargetAction fail: {reason}");
        _failed = true;
    }
}
