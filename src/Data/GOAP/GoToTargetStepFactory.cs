using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.GOAP;

[StepFactory]
public class GoToTargetStepFactory : IStepFactory
{
    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();
        foreach (TargetType type in Enum.GetValues<TargetType>())
        {
            // ALWAYS create movement steps - targets might become available during planning
            var preconds = new Dictionary<string, object> 
            { 
                { FactKeys.WorldHas(type), true }  // Target must exist in world
            };
            
            var effects = new Dictionary<string, object> 
            { 
                { FactKeys.NearTarget(type), true }  // Agent is now near target
            };

            // Cost based on state facts (estimated distance or heuristic)
            var costFactory = (State ctx) =>
            {
                // Use distance from state if available, otherwise use fixed cost
                string distKey = $"Distance_To_{type}";
                if (ctx.Facts.TryGetValue(distKey, out var distObj) && distObj is double dist)
                {
                    return dist;
                }
                // Default movement cost estimate
                return 10.0;
            };

            var step = new Step(
                actionFactory: () => new GoToTargetAction(type),
                preconditions: preconds,
                effects: effects,
                costFactory: costFactory
            );
            steps.Add(step);
        }
        return steps;
    }
}
