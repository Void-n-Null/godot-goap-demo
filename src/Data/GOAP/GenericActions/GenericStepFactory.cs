using System;
using System.Collections.Generic;
using Game.Data.Components;

namespace Game.Data.GOAP.GenericActions;

/// <summary>
/// Configuration for creating a generic GOAP step
/// </summary>
public class StepConfig
{
    public string Name { get; set; }
    public Func<IAction> ActionFactory { get; set; }
    
    // Changed to State
    public State Preconditions { get; set; } = State.Empty();
    
    // Changed to List
    public List<(string Key, object Value)> Effects { get; set; } = new();
    
    public Func<State, double> CostFactory { get; set; } = _ => 1.0;

    public StepConfig(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Unified step factory that creates parameterized steps instead of hardcoded ones.
/// Replaces all the individual XxxStepFactory classes.
/// </summary>

public class GenericStepFactory : IStepFactory
{
    private readonly List<StepConfig> _stepConfigs = [];

    public GenericStepFactory()
    {
        RegisterAllSteps();
    }

    private void RegisterAllSteps()
    {
        foreach (TargetType type in Enum.GetValues<TargetType>())
        {
            RegisterMoveToTargetStep(type);
            RegisterPickUpTargetStep(type);
        }

        RegisterChopTreeStep();
        RegisterConsumeFoodStep();
        RegisterBuildCampfireStep();
        RegisterIdleStep();
    }

    private void RegisterMoveToTargetStep(TargetType type)
    {
        var pre = State.Empty();
        pre.Set(FactKeys.WorldHas(type), true);

        var effects = new List<(string, object)>
        {
            (FactKeys.NearTarget(type), (FactValue)true)
        };

        bool requiresExclusiveUse = type != TargetType.Campfire;

        var config = new StepConfig($"GoTo_{type}")
        {
            ActionFactory = () => new MoveToEntityAction(
                EntityFinderConfig.ByTargetType(
                    type,
                    radius: 5000f,
                    requireUnreserved: requiresExclusiveUse,
                    shouldReserve: requiresExclusiveUse),
                reachDistance: 64f,
                actionName: $"GoTo_{type}"
            ),
            Preconditions = pre,
            Effects = effects,
            CostFactory = ctx =>
            {
                if (ctx.TryGet($"Distance_To_{type}", out var distObj))
                    return distObj.FloatValue / 100.0;
                return 10.0;
            }
        };

        _stepConfigs.Add(config);
    }

    private void RegisterIdleStep()
    {
        var config = new StepConfig("IdleAround")
        {
            ActionFactory = () => new IdleAction(durationSeconds: 1.0f),
            Preconditions = State.Empty(),
            Effects = new List<(string, object)>
            {
                ("IsIdle", (FactValue)true)
            },
            CostFactory = _ => 0.1f
        };

        _stepConfigs.Add(config);
    }

    private void RegisterPickUpTargetStep(TargetType type)
    {
        if (type == TargetType.Tree || type == TargetType.Bed || type == TargetType.Campfire) 
            return;

        var pre = State.Empty();
        pre.Set(FactKeys.WorldHas(type), true);
        pre.Set(FactKeys.NearTarget(type), true);

        var effects = new List<(string, object)>
        {
            (FactKeys.AgentCount(type), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.AgentCount(type), out var invObj))
                    return invObj.IntValue + 1;
                return 1;
            })),
            (FactKeys.AgentHas(type), (FactValue)true),
            (FactKeys.WorldCount(type), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.WorldCount(type), out var worldObj))
                    return Math.Max(0, worldObj.IntValue - 1);
                return 0;
            })),
            (FactKeys.WorldHas(type), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.WorldCount(type), out var worldObj))
                    return worldObj.IntValue - 1 > 0;
                return false;
            }))
        };

        var config = new StepConfig($"PickUp_{type}")
        {
            ActionFactory = () => new TimedInteractionAction(
                new EntityFinderConfig
                {
                    Filter = e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == type,
                    SearchRadius = 128f,
                    RequireUnreserved = true,
                    RequireReservation = false,
                    ShouldReserve = true
                },
                interactionTime: 0.5f,
                InteractionEffectConfig.PickUp(type),
                actionName: $"PickUp_{type}"
            ),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => 0.5
        };

        _stepConfigs.Add(config);
    }

    private void RegisterChopTreeStep()
    {
        var targetType = TargetType.Tree;
        var producedType = TargetType.Stick;

        var pre = State.Empty();
        pre.Set(FactKeys.NearTarget(targetType), true);
        pre.Set(FactKeys.WorldHas(targetType), true);

        var effects = new List<(string, object)>
        {
            (FactKeys.TargetChopped(targetType), (FactValue)true),
            (FactKeys.WorldHas(producedType), (FactValue)true),
            
            (FactKeys.WorldCount(producedType), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.WorldCount(producedType), out var wObj))
                    return wObj.IntValue + 4;
                return 4;
            })),
            
            (FactKeys.WorldCount(targetType), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.WorldCount(targetType), out var tObj))
                    return Math.Max(0, tObj.IntValue - 1);
                return 0;
            })),
            
            (FactKeys.WorldHas(targetType), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.WorldCount(targetType), out var tObj))
                    return tObj.IntValue - 1 > 0;
                return false;
            })),
            
            (FactKeys.NearTarget(targetType), (FactValue)false),
            (FactKeys.NearTarget(producedType), (FactValue)true)
        };

        var config = new StepConfig("ChopTree")
        {
            ActionFactory = () => new TimedInteractionAction(
                new EntityFinderConfig
                {
                    Filter = e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Tree,
                    SearchRadius = 128f,
                    RequireReservation = false,
                    ShouldReserve = true
                },
                interactionTime: 3.0f,
                InteractionEffectConfig.Kill(),
                actionName: "Chop"
            ),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => 3.0
        };

        _stepConfigs.Add(config);
    }

    private void RegisterConsumeFoodStep()
    {
        var pre = State.Empty();
        pre.Set(FactKeys.NearTarget(TargetType.Food), true);
        pre.Set(FactKeys.WorldHas(TargetType.Food), true);
        pre.Set("IsHungry", true);

        var effects = new List<(string, object)>
        {
            ("IsHungry", (FactValue)false),
            
            (FactKeys.WorldCount(TargetType.Food), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.WorldCount(TargetType.Food), out var wObj))
                    return Math.Max(0, wObj.IntValue - 1);
                return 0;
            })),

            (FactKeys.WorldHas(TargetType.Food), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.WorldCount(TargetType.Food), out var wObj))
                    return wObj.IntValue - 1 > 0;
                return false;
            })),

            (FactKeys.NearTarget(TargetType.Food), (FactValue)false)
        };

        var config = new StepConfig("ConsumeFood")
        {
            ActionFactory = () => new TimedInteractionAction(
                new EntityFinderConfig
                {
                    Filter = e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Food,
                    SearchRadius = 128f,
                    RequireReservation = false,
                    ShouldReserve = true
                },
                interactionTime: 2.0f,
                InteractionEffectConfig.ConsumeFood(),
                actionName: "ConsumeFood"
            ),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => 2.0
        };

        _stepConfigs.Add(config);
    }

    private void RegisterBuildCampfireStep()
    {
        var pre = State.Empty();
        pre.Set(FactKeys.AgentHas(TargetType.Stick), true);
        pre.Set(FactKeys.AgentCount(TargetType.Stick), 2); 
        
        var effects = new List<(string, object)>
        {
            ("HasCampfire", (FactValue)true),
            (FactKeys.WorldHas(TargetType.Campfire), (FactValue)true),
            (FactKeys.NearTarget(TargetType.Campfire), (FactValue)true),
            (FactKeys.WorldCount(TargetType.Campfire), (Func<State, FactValue>)(ctx =>
            {
                if (ctx.TryGet(FactKeys.WorldCount(TargetType.Campfire), out var existing))
                    return existing.IntValue + 1;
                return 1;
            })),
             (FactKeys.AgentCount(TargetType.Stick), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(FactKeys.AgentCount(TargetType.Stick), out var c))
                    return Math.Max(0, c.IntValue - 2);
                return 0;
            }))
        };

        var config = new StepConfig("BuildCampfire")
        {
            ActionFactory = () => new BuildCampfireAction(sticksRequired: 2, buildTime: 5.0f),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => 10.0
        };

        _stepConfigs.Add(config);
    }

    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();
        foreach (var config in _stepConfigs)
        {
            steps.Add(new Step(config.ActionFactory, config.Preconditions, config.Effects, config.CostFactory));
        }
        return steps;
    }
}
