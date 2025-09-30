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
    private float _timer;
    private const float PICKUP_TIME = 0.5f; // Half second to pick up

    public void Enter(RuntimeContext ctx)
    {
        const float PICKUP_RADIUS = 100f;
        var agentPos = ctx.Agent.Transform.Position;
        
        _timer = 0f;
        
        var nearby = ctx.World.EntityManager.QueryByComponent<TargetComponent>(agentPos, PICKUP_RADIUS)
            .Where(e => e.GetComponent<TargetComponent>().Target == _type)
            .ToList();
        
        _nearestTarget = nearby.FirstOrDefault();
        
        if (_nearestTarget == null)
        {
            GD.Print($"PickUpTargetAction.Enter: No {_type} found within {PICKUP_RADIUS}f of {agentPos}. Found {nearby.Count} candidates.");
            Fail($"No {_type} target nearby for PickUpTargetAction (searched {PICKUP_RADIUS}f radius)");
            _failed = true;
            return;
        }

        GD.Print($"PickUpTargetAction: Starting pickup of {_type} at {_nearestTarget.Transform.Position}...");
    }

    public ActionStatus Update(RuntimeContext ctx, float dt)
    {
        if (_failed || _nearestTarget == null) return ActionStatus.Failed;
        if (_completed) return ActionStatus.Succeeded;

        _timer += dt;
        
        // Wait for pickup time to elapse
        if (_timer >= PICKUP_TIME)
        {
            var agent = ctx.Agent;
            if (!agent.TryGetComponent<NPCData>(out var npcData))
            {
                Fail("Agent lacks NPCData for inventory");
                return ActionStatus.Failed;
            }

            // Pick up: Add to inventory
            npcData.Resources[_type] = (npcData.Resources.TryGetValue(_type, out var current) ? current : 0) + 1;
            GD.Print($"Picked up 1 {_type}(s); total: {npcData.Resources[_type]}");

            // Remove entity from world and destroy it
            ctx.World.EntityManager.UnregisterEntity(_nearestTarget);
            _nearestTarget.Destroy();

            _completed = true;
            GD.Print($"PickUpTargetAction succeeded for {_type} after {_timer:F2}s");
            return ActionStatus.Succeeded;
        }

        return ActionStatus.Running;
    }

    public void Exit(RuntimeContext ctx, ActionExitReason reason)
    {
        // No cleanup needed
    }

    public bool StillValid(RuntimeContext ctx)
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
