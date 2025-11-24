using System.Linq;
using Game.Data;
using Game.Data.Components;
using Game.Data.Crafting;
using Game.Universe;
using Godot;
using Game.Utils;

namespace Game.Data.GOAP.GenericActions;

/// <summary>
/// Generic action to build an item based on a CraftingRecipe.
/// </summary>
public sealed class BuildItemAction : PeriodicGuardAction
{
    private readonly CraftingRecipe _recipe;
    private float _timer;
    private bool _failed;
    private bool _completed;

    public BuildItemAction(CraftingRecipe recipe) : base(0.2f)
    {
        _recipe = recipe;
    }

    public override string Name => _recipe.Name;

    public override void Enter(Entity agent)
    {
        _timer = 0f;
        _completed = false;
        _failed = false;

        if (!agent.TryGetComponent<NPCData>(out var npcData))
        {
            Fail("Agent lacks NPCData");
            return;
        }

        if (!HasIngredients(npcData))
        {
            Fail($"Not enough resources to build {_recipe.OutputType}!");
            return;
        }

        if (_recipe.SpacingRadius > 0 && HasItemNearby(agent))
        {
            Fail($"Existing {_recipe.OutputType} already nearby; cannot build another.");
            return;
        }

        LM.Info($"[{agent.Name}] {_recipe.Name}: Starting construction (duration: {_recipe.BuildTime}s)");
    }

    public override ActionStatus Update(Entity agent, float dt)
    {
        if (_failed) return ActionStatus.Failed;
        if (_completed) return ActionStatus.Succeeded;

        _timer += dt;

        if (_timer >= _recipe.BuildTime)
        {
            if (!agent.TryGetComponent<NPCData>(out var npcData) ||
                !agent.TryGetComponent<TransformComponent2D>(out var transform))
            {
                Fail("Missing required components");
                return ActionStatus.Failed;
            }

            if (!HasIngredients(npcData))
            {
                Fail($"Lost resources during construction!");
                return ActionStatus.Failed;
            }

            // Consume ingredients
            ConsumeIngredients(npcData);

            // Spawn entity
            SpawnEntity.Now(
                _recipe.Blueprint,
                transform.Position
            );

            _completed = true;
            LM.Info($"Built {_recipe.OutputType} at {transform.Position}!");
            return ActionStatus.Succeeded;
        }

        return ActionStatus.Running;
    }

    public override void Exit(Entity agent, ActionExitReason reason)
    {
        if (reason != ActionExitReason.Completed)
        {
            // LM.Info($"{_recipe.Name} canceled before completion");
        }
    }

    public override bool StillValid(Entity agent)
    {
        if (_failed) return false;

        bool guardResult = EvaluateGuardPeriodically(agent, () =>
        {
            if (_recipe.SpacingRadius > 0 && HasItemNearby(agent))
            {
                LM.Info($"[{agent.Name}] {_recipe.Name} aborted - item already nearby.");
                return false;
            }
            return true;
        });

        if (!guardResult) return false;

        if (agent.TryGetComponent<NPCData>(out var npcData))
        {
            return HasIngredients(npcData);
        }

        return false;
    }

    public override void Fail(string reason)
    {
        LM.Error($"{_recipe.Name} fail: {reason}");
        _failed = true;
    }

    private bool HasIngredients(NPCData npcData)
    {
        foreach (var kvp in _recipe.Ingredients)
        {
            if (!npcData.Resources.TryGetValue(kvp.Key, out var count) || count < kvp.Value)
                return false;
        }
        return true;
    }

    private void ConsumeIngredients(NPCData npcData)
    {
        foreach (var kvp in _recipe.Ingredients)
        {
            if (npcData.Resources.ContainsKey(kvp.Key))
            {
                npcData.Resources[kvp.Key] -= kvp.Value;
            }
        }
    }

    private bool HasItemNearby(Entity agent)
    {
        if (!agent.TryGetComponent<TransformComponent2D>(out var transform))
            return false;

        var nearby = EntityManager.Instance?.SpatialPartition?.QueryCircle(
            transform.Position,
            _recipe.SpacingRadius,
            e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == _recipe.OutputType,
            maxResults: 1);

        return nearby != null && nearby.Count > 0;
    }
}

