using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using System.Linq;
using Godot;

namespace Game.Data.GOAP;

[StepFactory]
public class ChopTargetStepFactory : IStepFactory
{
    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();
        var targetType = TargetType.Tree;
        var targetName = targetType.ToString();

        var preconds = new Dictionary<string, object> { { $"At{targetName}", true }, { $"{targetName}Available", true } };
        var effects = new Dictionary<string, object>
        {
            { $"{targetName}Chopped", true },
            { "Available_Stick", true },  // Trees produce sticks when chopped
            { "WorldStickCount", (Func<State, object>)(ctx => {
                if (ctx.Facts.TryGetValue("WorldStickCount", out var wObj) && wObj is int w)
                    return w + 4; // Since 4 sticks per tree
                return 4;
            }) },
            { $"{targetName}Count", (Func<State, object>)(ctx => {
                if (ctx.Facts.TryGetValue($"{targetName}Count", out var tObj) && tObj is int t)
                    return Math.Max(0, t - 1);
                return 0;
            }) },
            { $"{targetName}Available", (Func<State, object>)(ctx => {
                if (ctx.Facts.TryGetValue($"{targetName}Count", out var tObj) && tObj is int t)
                    return t - 1 > 0;
                return false;
            }) },
            { $"At{targetName}", false }
            // Note: Don't set AtStick=true here - agent needs to go to sticks separately
        };

        var costFactory = (State ctx) => 5.0; // Fixed cost for 5 seconds of work

        var step = new Step(
            actionFactory: ctx => new ChopTargetAction(targetType),
            preconditions: preconds,
            effects: effects,
            costFactory: costFactory
        );
        steps.Add(step);

        return steps;
    }
}
