using System;
using System.Collections.Generic;
using Game.Data.Components;

namespace Game.Data.GOAP;

// DEPRECATED: Replaced by GenericStepFactory
// [StepFactory]
public class ConsumeFoodStepFactory_OLD // : IStepFactory
{
    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();

        var preconditions = new Dictionary<string, object>
        {
            { FactKeys.NearTarget(TargetType.Food), true }, // Must be near food
            { FactKeys.WorldHas(TargetType.Food), true }    // Food must exist
        };

        var effects = new Dictionary<string, object>
        {
            { "FoodConsumed", true },  // Eating goal satisfied
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
        };

        var costFactory = (State ctx) => 2.0; // 2 seconds to eat

        var step = new Step(
            actionFactory: () => new ConsumeFoodAction(),
            preconditions: preconditions,
            effects: effects,
            costFactory: costFactory
        );

        steps.Add(step);
        return steps;
    }
}
