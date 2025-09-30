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
        var producedType = TargetType.Stick; // Trees produce sticks

        // Only create step if trees actually exist in initial state
        if (!initialState.Facts.TryGetValue(FactKeys.WorldHas(targetType), out var available) || !(bool)available)
        {
            return steps; // No trees available to chop
        }

        var preconds = new Dictionary<string, object> 
        { 
            { FactKeys.NearTarget(targetType), true },  // Must be near tree
            { FactKeys.WorldHas(targetType), true }     // Tree must exist
        };
        
        var effects = new Dictionary<string, object>
        {
            { FactKeys.TargetChopped(targetType), true },
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
        };

        var costFactory = (State ctx) => 3.0; // Fixed cost for 3 seconds of chopping

        var step = new Step(
            actionFactory: () => new ChopTargetAction(targetType),
            preconditions: preconds,
            effects: effects,
            costFactory: costFactory
        );
        steps.Add(step);

        return steps;
    }
}
