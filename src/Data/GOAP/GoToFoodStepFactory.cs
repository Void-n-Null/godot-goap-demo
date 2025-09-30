using System.Collections.Generic;
using Game.Data.Components;

namespace Game.Data.GOAP;

[StepFactory]
public class GoToFoodStepFactory : IStepFactory
{
    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();

        var preconditions = new Dictionary<string, object>
        {
            { FactKeys.WorldHas(TargetType.Food), true } // Food must exist in world
        };

        var effects = new Dictionary<string, object>
        {
            { FactKeys.NearTarget(TargetType.Food), true } // Now near food
        };

        var costFactory = (State ctx) => 
        {
            // Get distance from state facts if available, otherwise assume average distance
            if (ctx.Facts.TryGetValue($"Distance_To_{TargetType.Food}", out var distObj) && distObj is float dist)
            {
                return dist / 100f; // Convert distance to time estimate
            }
            return 5.0; // Default cost
        };

        var step = new Step(
            actionFactory: () => new GoToFoodAction(),
            preconditions: preconditions,
            effects: effects,
            costFactory: costFactory
        );

        steps.Add(step);
        return steps;
    }
}
