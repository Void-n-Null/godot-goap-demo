using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.GOAP;

[StepFactory]
public class PickUpTargetStepFactory : IStepFactory
{
    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();
        foreach (TargetType type in Enum.GetValues<TargetType>())
        {
            // Filter to pickup-able targets only (not trees to chop, beds to use, etc.)
            if (type == TargetType.Tree || type == TargetType.Bed) continue;

            var targetName = type.ToString();

            // Only create steps for targets that actually exist in the world
            if (initialState.Facts.TryGetValue($"World{targetName}Count", out var countObj) && countObj is int count && count <= 0)
            {
                continue; // Skip if no such targets exist
            }

            var preconds = new Dictionary<string, object>
            {
                { $"Available_{targetName}", true },
                { $"At{targetName}", true }
            };
            var effects = new Dictionary<string, object>
            {
                { $"{targetName}Count", (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue($"{targetName}Count", out var invObj) && invObj is int inv)
                        return inv + 1;
                    return 1;
                }) },
                { $"World{targetName}Count", (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue($"World{targetName}Count", out var worldObj) && worldObj is int world)
                        return Math.Max(0, world - 1);
                    return 0;
                }) },
                { $"Available_{targetName}", (Func<State, object>)(ctx => {
                    if (ctx.Facts.TryGetValue($"World{targetName}Count", out var worldObj) && worldObj is int world)
                        return world - 1 > 0;
                    return false;
                }) },
                { $"At{targetName}", false }  // No longer at the pickup location after picking up
            };

            var costFactory = (State ctx) => 1.0; // Quick pick up

            var step = new Step(
                actionFactory: ctx => new PickUpTargetAction(type),
                preconditions: preconds,
                effects: effects,
                costFactory: costFactory
            );
            steps.Add(step);
        }

        return steps;
    }
}
