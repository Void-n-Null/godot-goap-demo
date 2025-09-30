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

            // ALWAYS create pickup steps - resources might become available during planning
            // (e.g., sticks appear after chopping trees)
            var preconds = new Dictionary<string, object>
            {
                { FactKeys.WorldHas(type), true },  // World must have this resource
                { FactKeys.NearTarget(type), true }  // Agent must be near it
            };
            
            var effects = new Dictionary<string, object>
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
            };

            var costFactory = (State ctx) => 0.5; // Quick pick up

            var step = new Step(
                actionFactory: () => new PickUpTargetAction(type),
                preconditions: preconds,
                effects: effects,
                costFactory: costFactory
            );
            steps.Add(step);
        }

        return steps;
    }
}
