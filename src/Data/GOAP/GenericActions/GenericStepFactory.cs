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
    public Dictionary<string, object> Preconditions { get; set; }
    public Dictionary<string, object> Effects { get; set; }
    public Func<State, double> CostFactory { get; set; }

    public StepConfig(string name)
    {
        Name = name;
        Preconditions = new Dictionary<string, object>();
        Effects = new Dictionary<string, object>();
        CostFactory = _ => 1.0;
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
        // For each target type, create movement and interaction steps
        foreach (TargetType type in Enum.GetValues<TargetType>())
        {
            RegisterMoveToTargetStep(type);
            RegisterPickUpTargetStep(type);
        }

        // Specialized steps
        RegisterChopTreeStep();
        RegisterConsumeFoodStep();
        RegisterBuildCampfireStep();
    }

    private void RegisterMoveToTargetStep(TargetType type)
    {
        var config = new StepConfig($"GoTo_{type}")
        {
            ActionFactory = () => new MoveToEntityAction(
                EntityFinderConfig.ByTargetType(type, requireUnreserved: true, shouldReserve: true),
                reachDistance: 64f,
                actionName: $"GoTo_{type}"
            ),
            Preconditions = new Dictionary<string, object>
            {
                { FactKeys.WorldHas(type), true }
            },
            Effects = new Dictionary<string, object>
            {
                { FactKeys.NearTarget(type), true }
            },
            CostFactory = ctx =>
            {
                // Use distance from state if available
                if (ctx.Facts.TryGetValue($"Distance_To_{type}", out var distObj) && distObj is double dist)
                    return dist / 100.0;
                return 10.0; // Default cost
            }
        };

        _stepConfigs.Add(config);
    }

    private void RegisterPickUpTargetStep(TargetType type)
    {
        // Filter to pickup-able targets only (not trees to chop, beds to use, campfires, etc.)
        if (type == TargetType.Tree || type == TargetType.Bed || type == TargetType.Campfire) 
            return;

        var config = new StepConfig($"PickUp_{type}")
        {
            ActionFactory = () => new TimedInteractionAction(
                new EntityFinderConfig
                {
                    Filter = e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == type,
                    SearchRadius = 100f,
                    // ✅ FIXED: Reserve on pickup (works with or without prior move step)
                    RequireUnreserved = false,  // May already be reserved by us from move
                    RequireReservation = false, // Don't require pre-existing reservation
                    ShouldReserve = true        // Reserve it ourselves when we start
                },
                interactionTime: 0.5f,
                InteractionEffectConfig.PickUp(type),
                actionName: $"PickUp_{type}"
            ),
            Preconditions = new Dictionary<string, object>
            {
                { FactKeys.WorldHas(type), true },  // World must have this resource
                { FactKeys.NearTarget(type), true }  // Agent must be near it
            },
            Effects = new Dictionary<string, object>
            {
                // Increment agent's inventory
                { FactKeys.AgentCount(type), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.AgentCount(type), out var invObj) && invObj is int inv)
                        return inv + 1;
                    return 1;
                }) },
                // Mark agent as having this resource type
                { FactKeys.AgentHas(type), true },
                // Decrement world count
                { FactKeys.WorldCount(type), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.WorldCount(type), out var worldObj) && worldObj is int world)
                        return Math.Max(0, world - 1);
                    return 0;
                }) },
                // Update world availability
                { FactKeys.WorldHas(type), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.WorldCount(type), out var worldObj) && worldObj is int world)
                        return world - 1 > 0;
                    return false;
                }) }
                // NOTE: Don't set Near_X to false - multiple items can be at same location
            },
            CostFactory = _ => 0.5 // Quick pick up
        };

        _stepConfigs.Add(config);
    }

    private void RegisterChopTreeStep()
    {
        var targetType = TargetType.Tree;
        var producedType = TargetType.Stick; // Trees produce sticks

        var config = new StepConfig("ChopTree")
        {
            ActionFactory = () => new TimedInteractionAction(
                new EntityFinderConfig
                {
                    Filter = e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Tree,
                    SearchRadius = 64f,
                    // ✅ Take ownership of reservation from GoTo_Tree to prevent leaks
                    RequireReservation = false,
                    ShouldReserve = true  // Re-reserve (idempotent if already reserved by us)
                },
                interactionTime: 3.0f,
                InteractionEffectConfig.Kill(),
                actionName: "Chop"
            ),
            Preconditions = new Dictionary<string, object>
            {
                { FactKeys.NearTarget(targetType), true },  // Must be near tree
                { FactKeys.WorldHas(targetType), true }     // Tree must exist
            },
            Effects = new Dictionary<string, object>
            {
                { FactKeys.TargetChopped(targetType), true }, // You chopped a tree
                { FactKeys.WorldHas(producedType), true },  // Sticks now available
                
                // Add 4 sticks to world
                { FactKeys.WorldCount(producedType), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.WorldCount(producedType), out var wObj) && wObj is int w)
                        return w + 4;
                    return 4;
                }) },
                
                // Decrement tree count
                { FactKeys.WorldCount(targetType), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.WorldCount(targetType), out var tObj) && tObj is int t)
                        return Math.Max(0, t - 1);
                    return 0;
                }) },
                
                // Update tree availability
                { FactKeys.WorldHas(targetType), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.WorldCount(targetType), out var tObj) && tObj is int t)
                        return t - 1 > 0;
                    return false;
                }) },
                
                // Tree is destroyed, so agent is no longer near it
                { FactKeys.NearTarget(targetType), false },
                
                // But sticks dropped at this location, so agent IS near sticks now
                { FactKeys.NearTarget(producedType), true }
            },
            CostFactory = _ => 3.0
        };

        _stepConfigs.Add(config);
    }

    private void RegisterConsumeFoodStep()
    {
        var config = new StepConfig("ConsumeFood")
        {
            ActionFactory = () => new TimedInteractionAction(
                new EntityFinderConfig
                {
                    Filter = e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == TargetType.Food,
                    SearchRadius = 100f,
                    // ✅ Take ownership of reservation from GoTo_Food to prevent leaks
                    RequireReservation = false,
                    ShouldReserve = true  // Re-reserve (idempotent if already reserved by us)
                },
                interactionTime: 2.0f,
                InteractionEffectConfig.ConsumeFood(),
                actionName: "ConsumeFood"
            ),
            Preconditions = new Dictionary<string, object>
            {
                { FactKeys.NearTarget(TargetType.Food), true },
                { FactKeys.WorldHas(TargetType.Food), true }
            },
            Effects = new Dictionary<string, object>
            {
                { "FoodConsumed", true }, // Goal satisfaction fact
                { FactKeys.WorldHas(TargetType.Food), (Func<State, object>)(ctx => {
                    // Food might still exist if there are more
                    if (ctx.Facts.TryGetValue(FactKeys.WorldCount(TargetType.Food), out var countObj) && countObj is int foodCount)
                        return foodCount - 1 > 0;
                    return false;
                })},
                { FactKeys.WorldCount(TargetType.Food), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.WorldCount(TargetType.Food), out var countObj) && countObj is int foodCount)
                        return Math.Max(0, foodCount - 1);
                    return 0;
                })},
                { FactKeys.NearTarget(TargetType.Food), false } // No longer near food (it's consumed)
            },
            CostFactory = _ => 2.0
        };

        _stepConfigs.Add(config);
    }

    private void RegisterBuildCampfireStep()
    {
        const int STICKS_REQUIRED = 16;

        var config = new StepConfig("BuildCampfire")
        {
            ActionFactory = () => new BuildCampfireAction(
                sticksRequired: STICKS_REQUIRED,
                buildTime: 3.0f
            ),
            Preconditions = new Dictionary<string, object>
            {
                { FactKeys.AgentCount(TargetType.Stick), STICKS_REQUIRED }, // Need 16 sticks
                // Don't require WorldHas(Campfire) = false, as building another is fine
            },
            Effects = new Dictionary<string, object>
            {
                { FactKeys.WorldHas(TargetType.Campfire), true }, // Campfire now exists
                { FactKeys.NearTarget(TargetType.Campfire), true }, // Agent is near the campfire they just built
                
                // Deduct sticks from inventory
                { FactKeys.AgentCount(TargetType.Stick), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.AgentCount(TargetType.Stick), out var invObj) && invObj is int inv)
                        return Math.Max(0, inv - STICKS_REQUIRED);
                    return 0;
                }) },
                
                // Update has sticks flag
                { FactKeys.AgentHas(TargetType.Stick), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.AgentCount(TargetType.Stick), out var invObj) && invObj is int inv)
                        return (inv - STICKS_REQUIRED) > 0;
                    return false;
                }) },
                
                // Increment world campfire count
                { FactKeys.WorldCount(TargetType.Campfire), (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue(FactKeys.WorldCount(TargetType.Campfire), out var wObj) && wObj is int w)
                        return w + 1;
                    return 1;
                }) }
            },
            CostFactory = _ => 3.0 // 3 seconds to build
        };

        _stepConfigs.Add(config);
    }

    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();

        foreach (var config in _stepConfigs)
        {
            var step = new Step(
                actionFactory: config.ActionFactory,
                preconditions: config.Preconditions,
                effects: config.Effects,
                costFactory: config.CostFactory
            );
            steps.Add(step);
        }

        return steps;
    }
}
