using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.GOAP;

public class ChopTreeStepFactory : IStepFactory
{
    public List<Step> CreateSteps(State initialState)
    {
        var steps = new List<Step>();
        var trees = initialState.World.EntityManager.QueryByTag(Tags.Tree, Vector2.Zero, float.MaxValue); // Get all trees
        GD.Print($"Found {trees.Count} trees");

        foreach (var tree in trees)
        {
            if (!tree.TryGetComponent<HealthComponent>(out _))
            {
                continue; // Skip non-choppable trees
            }

            var preconds = new Dictionary<string, object> { { "AtEntity", tree.Id.ToString() } };
            var effects = new Dictionary<string, object>
            {
                { "TreeChopped", tree.Id.ToString() },
                { "HasWood", true } // Assume chopping gives wood
            };

            var costFactory = (State ctx) => 5.0; // Fixed cost for 5 seconds of work

            var step = new Step(
                actionFactory: ctx => new ChopTreeAction(tree.Id),
                preconditions: preconds,
                effects: effects,
                costFactory: costFactory
            );
            steps.Add(step);
        }

        GD.Print($"ChopTreeStepFactory generated {steps.Count} chop steps");
        return steps;
    }
}
