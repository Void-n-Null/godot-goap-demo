using System.Linq;
using Game.Data.Components;
using Game.Utils;

namespace Game.Data.NPCActions;

/// <summary>
/// NPC action that finds the nearest non-burning flammable entity, moves towards it, and ignites it.
/// </summary>
public sealed class BurnFlammable : INPCAction
{
	private Entity _target;
	private FlammableComponent _flammable;
	private NPCMotorComponent _motor;

	public bool IsComplete { get; set; }

	public void Prepare(Entity entity)
	{
		_motor = entity.GetComponent<NPCMotorComponent>();
		if (_motor == null)
		{
			IsComplete = true;
			return;
		}

		_target = FindNearestFlammable(entity);
		if (_target == null)
		{
			IsComplete = true;
			return;
		}

		_flammable = _target.GetComponent<FlammableComponent>();
		if (_flammable == null || _flammable.IsBurning)
		{
			IsComplete = true;
			return;
		}

		_motor.OnTargetReached += IgniteTarget;
		_flammable.BurningStateChanged += OnFlammableStateChanged;
		_motor.Target = _target.Transform.Position;
	}

	public void OnUpdate(Entity entity, double delta)
	{
		if (IsComplete)
			return;

		if (_target == null || _flammable == null)
		{
			Complete();
			return;
		}

		if (_flammable.IsBurning)
		{
			Complete();
			return;
		}

		if (_target.Transform == null)
		{
			Complete();
			return;
		}

		_motor.Target = _target.Transform.Position;
	}

	public void OnStop(Entity entity)
	{
		if (_motor != null)
			_motor.OnTargetReached -= IgniteTarget;
		if (_flammable != null)
			_flammable.BurningStateChanged -= OnFlammableStateChanged;

		_target = null;
		_flammable = null;
		_motor = null;
	}

	private static Entity FindNearestFlammable(Entity seeker)
	{
		var origin = seeker.Transform?.Position ?? default;
		var candidates = GetEntities.MultiTryInRangeByPredicate(
			origin,
			attempts: 8,
			step: 100f,
			predicate: e => e != null && e.Transform != null && e.GetComponent<FlammableComponent>()?.IsBurning == false);

		return candidates
			?.OrderBy(e => e.Transform.Position.DistanceSquaredTo(origin))
			.FirstOrDefault();
	}

	private void IgniteTarget()
	{
		if (_flammable == null)
		{
			Complete();
			return;
		}

		_flammable.SetOnFire();
		Complete();
	}

	private void Complete()
	{
		IsComplete = true;
		if (_motor != null)
			_motor.OnTargetReached -= IgniteTarget;
		if (_flammable != null)
			_flammable.BurningStateChanged -= OnFlammableStateChanged;
	}

	private void OnFlammableStateChanged(FlammableComponent component, bool isBurning)
	{
		if (isBurning)
		{
			// Someone else ignited our target, complete the action
			Complete();
		}
	}
}
