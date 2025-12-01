using System;
using System.Collections.Generic;
using Game.Data.Components;
using Game.Data.Crafting;

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
        RegisterCraftingSteps();
        RegisterCookingSteps();
        RegisterSleepInBedStep();
        RegisterIdleStep();
    }

    private void RegisterSleepInBedStep()
    {
        var pre = State.Empty();
        pre.Set(FactKeys.NearTarget(TargetType.Bed), true);
        pre.Set(FactKeys.WorldHas(TargetType.Bed), true);
        pre.Set("IsSleepy", true);

        var effects = new List<(string, object)>
        {
            ("IsSleepy", (FactValue)false),
            (FactKeys.NearTarget(TargetType.Bed), (FactValue)false)
        };

        var config = new StepConfig("SleepInBed")
        {
            ActionFactory = () => new SleepInBedAction(),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => 1.0
        };

        _stepConfigs.Add(config);
    }

    private void RegisterMoveToTargetStep(TargetType type)
    {
        var pre = State.Empty();
        pre.Set(FactKeys.WorldHas(type), true);

        // Effects: Set NearTarget for this type, and clear NearTarget for key LOCATION types
        // Only clear for fixed locations (Campfire, Tree, Bed) - not pickupable items
        // This ensures the planner knows moving to X means you're no longer near fixed locations
        var effects = new List<(string, object)>
        {
            (FactKeys.NearTarget(type), (FactValue)true)
        };
        
        // Key location types that require physical presence
        // Includes fixed locations AND items dropped at fixed locations (like Stick from trees)
        var locationTypes = new[] { TargetType.Campfire, TargetType.Tree, TargetType.Bed, TargetType.Stick };
        
        foreach (var locationType in locationTypes)
        {
            if (locationType != type)
            {
                effects.Add((FactKeys.NearTarget(locationType), (FactValue)false));
            }
        }

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
        // Only allow consuming cooked food (Steak)
        RegisterConsumeItemStep(TargetType.Steak, "EatSteak");
    }

    private void RegisterConsumeItemStep(TargetType type, string stepName)
    {
        var pre = State.Empty();
        // Removed NearTarget requirement because we carry it
        pre.Set(FactKeys.AgentHas(type), true); 
        pre.Set("IsHungry", true);

        var effects = new List<(string, object)>
        {
            ("IsHungry", (FactValue)false),
            (FactKeys.AgentHas(type), (FactValue)false), // Consumed (assumes we only had 1 or we consume 1)
            (FactKeys.AgentCount(type), (Func<State, FactValue>)(ctx => {
                 if (ctx.TryGet(FactKeys.AgentCount(type), out var c))
                     return Math.Max(0, c.IntValue - 1);
                 return 0;
            }))
        };

        var config = new StepConfig(stepName)
        {
            ActionFactory = () => new ConsumeInventoryItemAction(type),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => 2.0
        };

        _stepConfigs.Add(config);
    }
    
    private void RegisterCookingSteps()
    {
        // 1. Deposit Raw Beef
        var depositPre = State.Empty();
        depositPre.Set(FactKeys.NearTarget(TargetType.Campfire), true);
        depositPre.Set(FactKeys.WorldHas(TargetType.Campfire), true);
        depositPre.Set(FactKeys.AgentHas(TargetType.RawBeef), true);
        // We assume campfire is free if we are going there to deposit. 
        // Logic for finding free campfire is in the Action.
        
        var depositEffects = new List<(string, object)>
        {
            (FactKeys.AgentHas(TargetType.RawBeef), (FactValue)false),
            (FactKeys.CampfireCooking(TargetType.RawBeef), (FactValue)true)
        };
        
        _stepConfigs.Add(new StepConfig("DepositRawBeef")
        {
            ActionFactory = () => new DepositItemAction(TargetType.RawBeef, TargetType.Steak),
            Preconditions = depositPre,
            Effects = depositEffects,
            CostFactory = _ => 1.0
        });

        // 2. Retrieve Steak
        var retrievePre = State.Empty();
        retrievePre.Set(FactKeys.NearTarget(TargetType.Campfire), true);
        retrievePre.Set(FactKeys.CampfireCooking(TargetType.RawBeef), true); 
        // We use "CampfireCooking(RawBeef)" as the state that enables retrieval of Steak.
        // It implies "Input was RawBeef, output will be Steak".
        
        var retrieveEffects = new List<(string, object)>
        {
             (FactKeys.CampfireCooking(TargetType.RawBeef), (FactValue)false),
             (FactKeys.AgentHas(TargetType.Steak), (FactValue)true),
             (FactKeys.AgentCount(TargetType.Steak), (Func<State, FactValue>)(ctx => {
                 if (ctx.TryGet(FactKeys.AgentCount(TargetType.Steak), out var c))
                     return c.IntValue + 1;
                 return 1;
             }))
        };
        
        _stepConfigs.Add(new StepConfig("RetrieveSteak")
        {
            ActionFactory = () => new RetrieveCookedItemAction(),
            Preconditions = retrievePre,
            Effects = retrieveEffects,
            CostFactory = _ => 10.0 // Higher cost to represent waiting time
        });
    }


    private void RegisterCraftingSteps()
    {
        foreach (var recipe in CraftingRegistry.Recipes)
    {
        var pre = State.Empty();
            foreach (var kvp in recipe.Ingredients)
            {
                pre.Set(FactKeys.AgentHas(kvp.Key), true);
                pre.Set(FactKeys.AgentCount(kvp.Key), kvp.Value);
            }

        var effects = new List<(string, object)>
        {
                (FactKeys.WorldHas(recipe.OutputType), (FactValue)true),
                (FactKeys.NearTarget(recipe.OutputType), (FactValue)true),
                (FactKeys.WorldCount(recipe.OutputType), (Func<State, FactValue>)(ctx =>
            {
                    if (ctx.TryGet(FactKeys.WorldCount(recipe.OutputType), out var existing))
                    return existing.IntValue + 1;
                return 1;
            }))
        };

            foreach (var kvp in recipe.Ingredients)
            {
                effects.Add((FactKeys.AgentCount(kvp.Key), (Func<State, FactValue>)(ctx => {
                    if (ctx.TryGet(FactKeys.AgentCount(kvp.Key), out var c))
                        return Math.Max(0, c.IntValue - kvp.Value);
                    return 0;
                })));
            }

            // For campfire, we add the legacy key just in case
            if (recipe.OutputType == TargetType.Campfire)
            {
                effects.Add(("HasCampfire", (FactValue)true));
            }

            var config = new StepConfig(recipe.Name)
        {
                ActionFactory = () => new BuildItemAction(recipe),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => 10.0
        };

        _stepConfigs.Add(config);
        }
    }

    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();
        foreach (var config in _stepConfigs)
        {
            var step = new Step(
                config.Name,
                config.ActionFactory,
                config.Preconditions,
                config.Effects,
                config.CostFactory
            );
            steps.Add(step);
        }
        return steps;
    }
}
