using System;
using System.Collections.Generic;
using System.Linq;
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
/// Now uses TargetTagRegistry for data-driven configuration.
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
        // Generate movement and pickup steps for all target tags
        foreach (var tag in Tags.TargetTags)
        {
            RegisterMoveToTargetStep(tag);
            
            var meta = TargetTagRegistry.Get(tag);
            if (meta.CanBePickedUp)
            {
                RegisterPickUpTargetStep(tag, meta);
            }
        }

        // Generate steps from registry definitions
        foreach (var harvest in TargetTagRegistry.Harvests)
        {
            RegisterHarvestStep(harvest);
        }
        
        foreach (var cooking in TargetTagRegistry.Cooking)
        {
            RegisterCookingSteps(cooking);
        }
        
        foreach (var consumable in TargetTagRegistry.Consumables)
        {
            RegisterConsumeStep(consumable);
        }

        // Crafting already uses CraftingRegistry (data-driven)
        RegisterCraftingSteps();
        
        // Special actions
        RegisterSleepInBedStep();
        RegisterIdleStep();
    }

    private void RegisterSleepInBedStep()
    {
        var pre = State.Empty();
        pre.Set(FactKeys.NearTarget(Tags.Bed), true);
        pre.Set(FactKeys.WorldHas(Tags.Bed), true);
        pre.Set("IsSleepy", true);

        var effects = new List<(string, object)>
        {
            ("IsSleepy", (FactValue)false),
            (FactKeys.NearTarget(Tags.Bed), (FactValue)false)
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

    private void RegisterMoveToTargetStep(Tag tag)
    {
        var meta = TargetTagRegistry.Get(tag);
        
        var pre = State.Empty();
        pre.Set(FactKeys.WorldHas(tag), true);

        // Effects: Set NearTarget for this tag, and clear NearTarget for ALL other tags
        // This is critical: the planner must understand that moving to X means you're NOT near Y anymore.
        // Without this, agents try to batch up "near" facts thinking they can be near multiple things.
        var effects = new List<(string, object)>
        {
            (FactKeys.NearTarget(tag), (FactValue)true)
        };
        
        // Clear NearTarget for ALL target tags when moving (except the destination)
        foreach (var otherTag in Tags.TargetTags)
        {
            if (otherTag != tag)
            {
                effects.Add((FactKeys.NearTarget(otherTag), (FactValue)false));
            }
        }

        var config = new StepConfig($"GoTo_{tag}")
        {
            ActionFactory = () => new MoveToEntityAction(
                EntityFinderConfig.ByTag(
                    tag,
                    radius: meta.MoveSearchRadius,
                    requireUnreserved: meta.RequiresExclusiveUse,
                    shouldReserve: meta.RequiresExclusiveUse),
                reachDistance: 64f,
                actionName: $"GoTo_{tag}"
            ),
            Preconditions = pre,
            Effects = effects,
            CostFactory = ctx =>
            {
                if (ctx.TryGet($"Distance_To_{tag}", out var distObj))
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

    private void RegisterPickUpTargetStep(Tag tag, TargetTagMetadata meta)
    {
        var pre = State.Empty();
        pre.Set(FactKeys.WorldHas(tag), true);
        pre.Set(FactKeys.NearTarget(tag), true);

        // Pre-compute keys and IDs outside lambdas to avoid string allocation per call
        var agentCountKey = FactKeys.AgentCount(tag);
        var agentCountId = FactRegistry.GetId(agentCountKey);
        var worldCountKey = FactKeys.WorldCount(tag);
        var worldCountId = FactRegistry.GetId(worldCountKey);

        var effects = new List<(string, object)>
        {
            (agentCountKey, (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(agentCountId, out var invObj))
                    return invObj.IntValue + 1;
                return 1;
            })),
            (FactKeys.AgentHas(tag), (FactValue)true),
            (worldCountKey, (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(worldCountId, out var worldObj))
                    return Math.Max(0, worldObj.IntValue - 1);
                return 0;
            })),
            (FactKeys.WorldHas(tag), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(worldCountId, out var worldObj))
                    return worldObj.IntValue - 1 > 0;
                return false;
            }))
        };

        var config = new StepConfig($"PickUp_{tag}")
        {
            ActionFactory = () => new TimedInteractionAction(
                new EntityFinderConfig
                {
                    Filter = e => e.HasTag(tag),
                    SearchRadius = meta.InteractionRadius,
                    RequireUnreserved = true,
                    RequireReservation = false,
                    ShouldReserve = true
                },
                interactionTime: meta.PickupTime,
                InteractionEffectConfig.PickUp(tag),
                actionName: $"PickUp_{tag}"
            ),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => 0.5
        };

        _stepConfigs.Add(config);
    }
    
    private void RegisterHarvestStep(HarvestDefinition harvest)
    {
        var pre = State.Empty();
        pre.Set(FactKeys.NearTarget(harvest.SourceTag), true);
        pre.Set(FactKeys.WorldHas(harvest.SourceTag), true);

        // Pre-compute keys and IDs outside lambdas to avoid string allocation per call
        var producedCountKey = FactKeys.WorldCount(harvest.ProducedTag);
        var producedCountId = FactRegistry.GetId(producedCountKey);
        var sourceCountKey = FactKeys.WorldCount(harvest.SourceTag);
        var sourceCountId = FactRegistry.GetId(sourceCountKey);
        var producedCount = harvest.ProducedCount; // Capture value

        var effects = new List<(string, object)>
        {
            (FactKeys.TargetChopped(harvest.SourceTag), (FactValue)true),
            (FactKeys.WorldHas(harvest.ProducedTag), (FactValue)true),

            (producedCountKey, (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(producedCountId, out var wObj))
                    return wObj.IntValue + producedCount;
                return producedCount;
            })),

            (sourceCountKey, (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(sourceCountId, out var tObj))
                    return Math.Max(0, tObj.IntValue - 1);
                return 0;
            })),

            (FactKeys.WorldHas(harvest.SourceTag), (Func<State, FactValue>)(ctx => {
                if (ctx.TryGet(sourceCountId, out var tObj))
                    return tObj.IntValue - 1 > 0;
                return false;
            })),

            (FactKeys.NearTarget(harvest.SourceTag), (FactValue)false),
            (FactKeys.NearTarget(harvest.ProducedTag), (FactValue)true)
        };

        var config = new StepConfig(harvest.Name)
        {
            ActionFactory = () => new TimedInteractionAction(
                new EntityFinderConfig
                {
                    Filter = e => e.HasTag(harvest.SourceTag),
                    SearchRadius = 128f,
                    RequireReservation = false,
                    ShouldReserve = true
                },
                interactionTime: harvest.InteractionTime,
                harvest.DestroySource ? InteractionEffectConfig.Kill() : InteractionEffectConfig.None(),
                actionName: harvest.Name.Replace("Chop", "Chop ").Replace("Mine", "Mine ").Trim()
            ),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => harvest.Cost
        };

        _stepConfigs.Add(config);
    }
    
    private void RegisterCookingSteps(CookingDefinition cooking)
    {
        // 1. Deposit step
        var depositPre = State.Empty();
        depositPre.Set(FactKeys.NearTarget(cooking.StationTag), true);
        depositPre.Set(FactKeys.WorldHas(cooking.StationTag), true);
        depositPre.Set(FactKeys.AgentHas(cooking.InputTag), true);
        
        var depositEffects = new List<(string, object)>
        {
            (FactKeys.AgentHas(cooking.InputTag), (FactValue)false),
            (FactKeys.CampfireCooking(cooking.InputTag), (FactValue)true)
        };
        
        _stepConfigs.Add(new StepConfig(cooking.DepositStepName)
        {
            ActionFactory = () => new DepositItemAction(cooking.InputTag, cooking.OutputTag),
            Preconditions = depositPre,
            Effects = depositEffects,
            CostFactory = _ => cooking.DepositCost
        });

        // 2. Retrieve step - pre-compute keys/IDs outside lambda
        var outputCountKey = FactKeys.AgentCount(cooking.OutputTag);
        var outputCountId = FactRegistry.GetId(outputCountKey);
        
        var retrievePre = State.Empty();
        retrievePre.Set(FactKeys.NearTarget(cooking.StationTag), true);
        retrievePre.Set(FactKeys.CampfireCooking(cooking.InputTag), true);
        
        var retrieveEffects = new List<(string, object)>
        {
             (FactKeys.CampfireCooking(cooking.InputTag), (FactValue)false),
             (FactKeys.AgentHas(cooking.OutputTag), (FactValue)true),
             (outputCountKey, (Func<State, FactValue>)(ctx => {
                 if (ctx.TryGet(outputCountId, out var c))
                     return c.IntValue + 1;
                 return 1;
             }))
        };
        
        _stepConfigs.Add(new StepConfig(cooking.RetrieveStepName)
        {
            ActionFactory = () => new RetrieveCookedItemAction(),
            Preconditions = retrievePre,
            Effects = retrieveEffects,
            CostFactory = _ => cooking.RetrieveCost
        });
    }
    
    private void RegisterConsumeStep(ConsumableDefinition consumable)
    {
        var pre = State.Empty();
        pre.Set(FactKeys.AgentHas(consumable.ItemTag), true);
        pre.Set(consumable.SatisfiesFact, !consumable.SatisfiesValue); // e.g., "IsHungry" = true (means we ARE hungry)

        // Pre-compute keys and IDs outside lambda
        var agentCountKey = FactKeys.AgentCount(consumable.ItemTag);
        var agentCountId = FactRegistry.GetId(agentCountKey);

        var effects = new List<(string, object)>
        {
            (consumable.SatisfiesFact, (FactValue)consumable.SatisfiesValue),
            (FactKeys.AgentHas(consumable.ItemTag), (FactValue)false),
            (agentCountKey, (Func<State, FactValue>)(ctx => {
                 if (ctx.TryGet(agentCountId, out var c))
                     return Math.Max(0, c.IntValue - 1);
                 return 0;
            }))
        };

        _stepConfigs.Add(new StepConfig(consumable.StepName)
        {
            ActionFactory = () => new ConsumeInventoryItemAction(consumable.ItemTag),
            Preconditions = pre,
            Effects = effects,
            CostFactory = _ => consumable.Cost
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

            // Pre-compute keys and IDs outside lambdas
            var worldCountKey = FactKeys.WorldCount(recipe.OutputTag);
            var worldCountId = FactRegistry.GetId(worldCountKey);

            var effects = new List<(string, object)>
            {
                (FactKeys.WorldHas(recipe.OutputTag), (FactValue)true),
                (FactKeys.NearTarget(recipe.OutputTag), (FactValue)true),
                (worldCountKey, (Func<State, FactValue>)(ctx =>
                {
                    if (ctx.TryGet(worldCountId, out var existing))
                        return existing.IntValue + 1;
                    return 1;
                }))
            };

            foreach (var kvp in recipe.Ingredients)
            {
                // Pre-compute for each ingredient
                var ingredientCountKey = FactKeys.AgentCount(kvp.Key);
                var ingredientCountId = FactRegistry.GetId(ingredientCountKey);
                var consumeAmount = kvp.Value; // Capture value to avoid closure over kvp
                
                effects.Add((ingredientCountKey, (Func<State, FactValue>)(ctx => {
                    if (ctx.TryGet(ingredientCountId, out var c))
                        return Math.Max(0, c.IntValue - consumeAmount);
                    return 0;
                })));
            }

            // For campfire, we add the legacy key just in case
            if (recipe.OutputTag == Tags.Campfire)
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
