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
            var targetName = type.ToString();

            // Only create steps for targets that actually exist in the world
            if (initialState.Facts.TryGetValue($"World{targetName}Count", out var countObj) && countObj is int count && count <= 0)
            {
                continue; // Skip if no such targets exist
            }

            var preconds = new Dictionary<string, object> { { $"Available_{targetName}", true } };
            var effects = new Dictionary<string, object> { { $"At{targetName}", true } };

            var costFactory = (State ctx) =>
            {
                if (ctx.World?.EntityManager == null)
                    return double.PositiveInfinity;

                if (!ctx.Agent.TryGetComponent<TransformComponent2D>(out var agentTransform))
                    return double.PositiveInfinity;

                var nearest = ctx.World.EntityManager.QueryByComponent<TargetComponent>(agentTransform.Position, float.MaxValue)
                    .FirstOrDefault(e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == type);

                if (nearest == null || !nearest.TryGetComponent<TransformComponent2D>(out var targetTransform))
                    return double.PositiveInfinity;

                return agentTransform.Position.DistanceTo(targetTransform.Position);
            };

            var step = new Step(
                actionFactory: ctx => new GoToTargetAction(type),
                preconditions: preconds,
                effects: effects,
                costFactory: costFactory
            );
            steps.Add(step);
        }
        return steps;
    }
}
