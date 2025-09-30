using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Godot;

namespace Game.Data.GOAP;

public sealed class GoToFoodAction : IAction, IRuntimeGuard
{
    private Entity _foodTarget;
    private bool _failed;

    public void Enter(RuntimeContext ctx)
    {
        var agent = ctx.Agent;
        var agentPos = agent.Transform.Position;

        // Find nearest unreserved food
        _foodTarget = EntityManager.Instance.AllEntities.OfType<Entity>()
            .Where(e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Food)
            .Where(e => ResourceReservationManager.Instance.IsAvailableFor(e, agent))
            .OrderBy(e => agentPos.DistanceTo(e.Transform.Position))
            .FirstOrDefault();

        if (_foodTarget == null)
        {
            GD.Print($"[{agent.Name}] GoToFoodAction: No unreserved food available");
            Fail("No unreserved food found in world");
            return;
        }

        // Reserve the food
        if (!ResourceReservationManager.Instance.TryReserve(_foodTarget, agent))
        {
            Fail("Failed to reserve food");
            return;
        }

        GD.Print($"[{agent.Name}] GoToFoodAction: Moving to {_foodTarget.Name} at {_foodTarget.Transform.Position}");
    }

    public ActionStatus Update(RuntimeContext ctx, float dt)
    {
        if (_failed || _foodTarget == null)
            return ActionStatus.Failed;

        var agentPos = ctx.Agent.Transform.Position;
        var foodPos = _foodTarget.Transform.Position;
        var distance = agentPos.DistanceTo(foodPos);

        const float REACH_DISTANCE = 64f;

        if (distance <= REACH_DISTANCE)
        {
            GD.Print($"GoToFoodAction: Reached food at {foodPos}");
            return ActionStatus.Succeeded;
        }

        // Move toward food
        if (ctx.Agent.TryGetComponent<NPCMotorComponent>(out var motor))
        {
            motor.Target = foodPos;
        }

        return ActionStatus.Running;
    }

    public void Exit(RuntimeContext ctx, ActionExitReason reason)
    {
        // Stop movement
        if (ctx.Agent.TryGetComponent<NPCMotorComponent>(out var motor))
        {
            motor.Target = null;
        }

        // Release reservation if we didn't complete
        if (reason != ActionExitReason.Completed && _foodTarget != null)
        {
            ResourceReservationManager.Instance.Release(_foodTarget, ctx.Agent);
        }
    }

    public bool StillValid(RuntimeContext ctx)
    {
        if (_failed) return false;
        // Check if food still exists
        return _foodTarget != null && EntityManager.Instance.AllEntities.Contains(_foodTarget);
    }

    public void Fail(string reason)
    {
        GD.PushError($"GoToFoodAction fail: {reason}");
        _failed = true;
    }
}
