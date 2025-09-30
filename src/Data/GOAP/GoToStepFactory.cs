using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.GOAP;

public class GoToStepFactory : IStepFactory
{
	public List<Step> CreateSteps(State initialState)
	{
		var steps = new List<Step>();
		var agent = initialState.Agent;
		if (!agent.TryGetComponent<TransformComponent2D>(out var agentTransform))
		{
			GD.PushWarning("Agent lacks TransformComponent2D; cannot create go-to steps");
			return steps;
		}

		var agentPos = agentTransform.Position;
		if (initialState.World?.EntityManager == null)
		{
			return steps;
		}
		var allEntities = initialState.World.EntityManager.AllEntities.OfType<Entity>().ToList();
		foreach (var targetEntity in allEntities)
		{
			if (targetEntity.Id == agent.Id) continue; // Skip self

			if (!targetEntity.TryGetComponent<TransformComponent2D>(out var targetTransform))
				continue; // Skip entities without position

			var targetPos = targetTransform.Position;
			var distance = agentPos.DistanceTo(targetPos);

			var preconds = new Dictionary<string, object> { { "NeedsToApproach", true } };
			var effects = new Dictionary<string, object> 
			{ 
				{ "AtEntity", targetEntity.Id.ToString() } 
			};

			// Add tag-based facts: e.g., {"AtTree", true} if target has Tree tag
			foreach (var tag in targetEntity.Tags)
			{
				effects[$"At{tag}"] = true;
			}

			// Add component-based facts: e.g., {"AtFlammable", true} if has FlammableComponent
			if (targetEntity.TryGetComponent<FlammableComponent>(out _))
				effects["AtFlammable"] = true;
			if (targetEntity.TryGetComponent<FoodData>(out _))
				effects["AtFood"] = true;
			// Add more as needed, e.g., for HealthComponent: "AtHealthyEntity", etc.

			var costFactory = (State ctx) =>
			{
				if (!ctx.Agent.TryGetComponent<TransformComponent2D>(out var ctxAgentTransform))
					return double.PositiveInfinity;

				var targetEntityCtx = initialState.World.EntityManager.GetEntityById(targetEntity.Id);
				if (targetEntityCtx == null || !targetEntityCtx.TryGetComponent<TransformComponent2D>(out var ctxTargetTransform))
					return double.PositiveInfinity;

				return ctxAgentTransform.Position.DistanceTo(ctxTargetTransform.Position);
			};

			var step = new Step(
				actionFactory: ctx => new GoToAction(targetEntity.Id),
				preconditions: preconds,
				effects: effects,
				costFactory: costFactory
			);
			steps.Add(step);
		}
		return steps;
	}

	private void AddResourceGoToSteps(State initialState, List<Step> steps)
	{
		if (!initialState.Facts.TryGetValue("StickAvailable", out var available) || !(bool)available) return;

		var preconds = new Dictionary<string, object> { { "StickAvailable", true } };
		var effects = new Dictionary<string, object> { { "AtStick", true } };
		var costFactory = (State ctx) =>
		{
			var nearest = ctx.World.EntityManager.QueryByComponent<TargetComponent>(ctx.Agent.Transform.Position, float.MaxValue)
				.Where(e => e.GetComponent<TargetComponent>().Target == TargetType.Stick).FirstOrDefault();
			if (nearest == null) return double.PositiveInfinity;
			return ctx.Agent.GetComponent<TransformComponent2D>().Position.DistanceTo(nearest.GetComponent<TransformComponent2D>().Position);
		};

		var step = new Step(
			actionFactory: ctx => new GoToTargetAction(TargetType.Stick), // New action that finds nearest at runtime
			preconditions: preconds,
			effects: effects,
			costFactory: costFactory
		);
		steps.Add(step);
	}
}
