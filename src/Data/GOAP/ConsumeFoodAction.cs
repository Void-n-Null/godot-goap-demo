using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Godot;
using Game.Utils;

namespace Game.Data.GOAP;

public sealed class ConsumeFoodAction : IAction, IRuntimeGuard
{
    private Entity _foodTarget;
    private bool _failed;
    private float _timer;
    private const float EAT_TIME = 2f; // 2 seconds to eat

    public string Name => $"Consume {_foodTarget?.Name}";

    public void Enter(Entity agent)
    {
        _timer = 0f;
        var agentPos = agent.Transform.Position;
        const float PICKUP_RADIUS = 100f;

        // Find nearest food that we have reserved
        _foodTarget = ServiceLocator.EntityManager.QueryByComponent<TargetComponent>(agentPos, PICKUP_RADIUS)
            .Where(e => e.GetComponent<TargetComponent>().Target == TargetType.Food)
            .Where(e => ResourceReservationManager.Instance.IsReservedBy(e, agent))
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

        LM.Info($"ConsumeFoodAction: Starting to eat {_foodTarget.Name} (restores {foodData.HungerRestoredOnConsumption} hunger)");
    }

    public ActionStatus Update(Entity agent, float dt)
    {
        if (_failed || _foodTarget == null)
            return ActionStatus.Failed;

        _timer += dt;

        if (_timer >= EAT_TIME)
        {
            // Get food data
            var foodData = _foodTarget.GetComponent<FoodData>();
            var npcData = agent.GetComponent<NPCData>();

            // Restore hunger
            float hungerRestored = foodData.HungerRestoredOnConsumption;
            using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Godot;
using Game.Utils;

namespace Game.Data.GOAP;

public sealed class ConsumeFoodAction : IAction, IRuntimeGuard
{
    private Entity _foodTarget;
    private bool _failed;
    private float _timer;
    private const float EAT_TIME = 2f; // 2 seconds to eat

    public string Name => $"Consume {_foodTarget?.Name}";

    public void Enter(Entity agent)
    {
        _timer = 0f;
        var agentPos = agent.Transform.Position;
        const float PICKUP_RADIUS = 100f;

        // Find nearest food that we have reserved
        _foodTarget = ServiceLocator.EntityManager.QueryByComponent<TargetComponent>(agentPos, PICKUP_RADIUS)
            .Where(e => e.GetComponent<TargetComponent>().Target == TargetType.Food)
            .Where(e => ResourceReservationManager.Instance.IsReservedBy(e, agent))
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

        LM.Info($"ConsumeFoodAction: Starting to eat {_foodTarget.Name} (restores {foodData.HungerRestoredOnConsumption} hunger)");
    }

    public ActionStatus Update(Entity agent, float dt)
    {
        if (_failed || _foodTarget == null)
            return ActionStatus.Failed;

        _timer += dt;

        if (_timer >= EAT_TIME)
        {
            // Get food data
            var foodData = _foodTarget.GetComponent<FoodData>();
            var npcData = agent.GetComponent<NPCData>();

            // Restore hunger
            float hungerRestored = foodData.HungerRestoredOnConsumption;
            npcData.Hunger = Mathf.Max(0, npcData.Hunger - hungerRestored);

            LM.Info($"Consumed {_foodTarget.Name}! Hunger reduced by {hungerRestored}. Current hunger: {npcData.Hunger}/{npcData.MaxHunger}");

            // Release reservation before destroying
            ResourceReservationManager.Instance.Release(_foodTarget, agent);

            // Remove food from world
            ServiceLocator.EntityManager.UnregisterEntity(_foodTarget);
            _foodTarget.Destroy();

            return ActionStatus.Succeeded;
        }

        return ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason)
    {
        if (reason != ActionExitReason.Completed)
        {
            LM.Info("ConsumeFoodAction canceled before eating finished");
        }
    }

    public bool StillValid(Entity agent)
    {
        if (_failed) return false;
        // Use IsActive instead of expensive linear search through all entities
        return _foodTarget != null && _foodTarget.IsActive;
    }

    public void Fail(string reason)
    {
        LM.Error($"ConsumeFoodAction fail: {reason}");
        _failed = true;
    }
}npcData.Hunger = Mathf.Max(0, npcData.Hunger - hungerRestored);

            LM.Info($"Consumed {_foodTarget.Name}! Hunger reduced by {hungerRestored}. Current hunger: {npcData.Hunger}/{npcData.MaxHunger}");

            // Release reservation before destroying
            ResourceReservationManager.Instance.Release(_foodTarget, agent);

            // Remove food from world
            ServiceLocator.EntityManager.UnregisterEntity(_foodTarget);
            _foodTarget.Destroy();

            return ActionStatus.Succeeded;
        }

        return ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason)
    {
        if (reason != ActionExitReason.Completed)
        {
            LM.Info("ConsumeFoodAction canceled before eating finished");
        }
    }

    public bool StillValid(Entity agent)
    {
        if (_failed) return false;
        // Use IsActive instead of expensive linear search through all entities
        return _foodTarget != null && _foodTarget.IsActive;
    }

    public void Fail(string reason)
    {
        LM.Error($"ConsumeFoodAction fail: {reason}");
        _failed = true;
    }
}
