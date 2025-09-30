using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Godot;

namespace Game.Data.GOAP;

public sealed class ConsumeFoodAction : IAction, IRuntimeGuard
{
    private Entity _foodTarget;
    private bool _failed;
    private float _timer;
    private const float EAT_TIME = 2f; // 2 seconds to eat

    public void Enter(RuntimeContext ctx)
    {
        _timer = 0f;
        var agentPos = ctx.Agent.Transform.Position;
        const float PICKUP_RADIUS = 100f;

        // Find nearest food that we have reserved
        _foodTarget = ctx.World.EntityManager.QueryByComponent<TargetComponent>(agentPos, PICKUP_RADIUS)
            .Where(e => e.GetComponent<TargetComponent>().Target == TargetType.Food)
            .Where(e => ResourceReservationManager.Instance.IsReservedBy(e, ctx.Agent))
            .FirstOrDefault();

        if (_foodTarget == null)
        {
            Fail("No reserved food nearby to consume");
            return;
        }

        if (!_foodTarget.TryGetComponent<FoodData>(out var foodData))
        {
            Fail("Food entity lacks FoodData component");
            return;
        }

        GD.Print($"ConsumeFoodAction: Starting to eat {_foodTarget.Name} (restores {foodData.HungerRestoredOnConsumption} hunger)");
    }

    public ActionStatus Update(RuntimeContext ctx, float dt)
    {
        if (_failed || _foodTarget == null)
            return ActionStatus.Failed;

        _timer += dt;

        if (_timer >= EAT_TIME)
        {
            // Get food data
            var foodData = _foodTarget.GetComponent<FoodData>();
            var npcData = ctx.Agent.GetComponent<NPCData>();

            // Restore hunger
            float hungerRestored = foodData.HungerRestoredOnConsumption;
            npcData.Hunger = Mathf.Max(0, npcData.Hunger - hungerRestored);

            GD.Print($"Consumed {_foodTarget.Name}! Hunger reduced by {hungerRestored}. Current hunger: {npcData.Hunger}/{npcData.MaxHunger}");

            // Release reservation before destroying
            ResourceReservationManager.Instance.Release(_foodTarget, ctx.Agent);

            // Remove food from world
            ctx.World.EntityManager.UnregisterEntity(_foodTarget);
            _foodTarget.Destroy();

            return ActionStatus.Succeeded;
        }

        return ActionStatus.Running;
    }

    public void Exit(RuntimeContext ctx, ActionExitReason reason)
    {
        if (reason != ActionExitReason.Completed)
        {
            GD.Print("ConsumeFoodAction canceled before eating finished");
        }
    }

    public bool StillValid(RuntimeContext ctx)
    {
        if (_failed) return false;
        return _foodTarget != null && EntityManager.Instance.AllEntities.Contains(_foodTarget);
    }

    public void Fail(string reason)
    {
        GD.PushError($"ConsumeFoodAction fail: {reason}");
        _failed = true;
    }
}
