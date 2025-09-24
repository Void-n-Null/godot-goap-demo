using System;
using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Game.Utils;

namespace Game.Data.NPCActions;

/// <summary>
/// NPC action that seeks the nearest entity tagged as food, moves toward it, consumes it,
/// restores hunger, and removes the consumed food entity from the world.
/// Requires the NPC to have an <see cref="NPCMotorComponent"/> and <see cref="NPCData"/>.
/// </summary>
public sealed class ConsumeNearestFood : INPCAction
{
	private Entity _target;
	private NPCMotorComponent _motor;
	private NPCData _npcData;
	private FoodData _foodData;

	public bool IsComplete { get; set; }

	public void Prepare(Entity entity)
	{
		_motor = entity.GetComponent<NPCMotorComponent>();
		_npcData = entity.GetComponent<NPCData>();

		if (_motor == null || _npcData == null)
		{
			IsComplete = true;
			return;
		}

		_target = FindNearestFood(entity);
		if (_target == null)
		{
			IsComplete = true;
			return;
		}

		_foodData = _target.GetComponent<FoodData>();
		if (_foodData == null)
		{
			IsComplete = true;
			return;
		}

		_motor.OnTargetReached += ConsumeTarget;
		_foodData.FoodConsumed += OnFoodConsumed;
		_motor.Target = _target.Transform.Position;
	}

	public void OnUpdate(Entity entity, double delta)
	{
		if (IsComplete)
			return;

		if (_target == null || _motor == null)
		{
			Complete();
			return;
		}

		if (_target.Transform == null)
		{
			Complete();
			return;
		}

		if (!_target.Tags.Contains(Tags.Food))
		{
			Complete();
			return;
		}

		_motor.Target = _target.Transform.Position;
	}

	public void OnStop(Entity entity)
	{
		if (_motor != null)
			_motor.OnTargetReached -= ConsumeTarget;
		if (_foodData != null)
			_foodData.FoodConsumed -= OnFoodConsumed;

		_target = null;
		_motor = null;
		_npcData = null;
		_foodData = null;
	}

	private Entity FindNearestFood(Entity seeker)
	{
		var origin = seeker.Transform?.Position ?? default;
		var candidates = GetEntities.MultiTryInRangeByComponent<FoodData>(
			origin,
			attempts: 8,
			step: 100f);

		return candidates
			?.Where(e => e != null && e.Transform != null)
			.OrderBy(e => e.Transform.Position.DistanceSquaredTo(origin))
			.FirstOrDefault();
	}

	private void ConsumeTarget()
	{
		if (_target == null || _npcData == null || _foodData == null)
		{
			Complete();
			return;
		}

		float restoreAmount = _foodData.HungerRestoredOnConsumption;
		_npcData.Hunger = Math.Max(0f, _npcData.Hunger - restoreAmount);

		// Notify others that this food is being consumed
		_foodData.MarkAsConsumed();

		EntityManager.Instance.UnregisterEntity(_target);
		_target.Destroy();

		Complete();
	}

	private void Complete()
	{
		IsComplete = true;
		if (_motor != null)
			_motor.OnTargetReached -= ConsumeTarget;
		if (_foodData != null)
			_foodData.FoodConsumed -= OnFoodConsumed;
	}

	private void OnFoodConsumed(FoodData food)
	{
		// Someone else consumed our target food, complete the action
		Complete();
	}
}
