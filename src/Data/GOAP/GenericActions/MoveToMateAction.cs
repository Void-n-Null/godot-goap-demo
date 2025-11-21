using System;
using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Godot;

namespace Game.Data.GOAP.GenericActions;

public sealed class MoveToMateAction(float searchRadius = 2000f, float reachDistance = 64f) : PeriodicGuardAction(0.4f)
{
	private readonly float _searchRadius = searchRadius;
	private readonly float _reachDistance = reachDistance;

	private Entity _mateTarget;
	private NPCMotorComponent _motor;
	private bool _arrived;
	private bool _failed;

	public override string Name => "GoToMate";

	public override void Enter(Entity agent)
	{
		if (!agent.TryGetComponent(out _motor))
		{
			Fail("Agent lacks NPCMotorComponent");
			return;
		}

		var npcData = agent.GetComponent<NPCData>();
		if (npcData == null || !npcData.ShouldSeekMate)
		{
			Fail("Agent not ready to mate");
			return;
		}

		_mateTarget = FindMateCandidate(agent, npcData);
		if (_mateTarget == null)
		{
			Fail("No compatible mate found");
			return;
		}

		if (!ResourceReservationManager.Instance.TryReserve(_mateTarget, agent))
		{
			Fail("Failed to reserve mate candidate");
			return;
		}

        npcData.SetActiveMate(_mateTarget);
        if (_mateTarget.TryGetComponent<NPCData>(out var partnerData) && !partnerData.HasActiveMate)
        {
            partnerData.SetActiveMate(agent);
        }
		_motor.OnTargetReached += OnArrived;
		_motor.Target = _mateTarget.Transform.Position;
	}

	private Entity FindMateCandidate(Entity agent, NPCData agentData)
	{
		var candidates = ServiceLocator.EntityManager
			.QueryByComponent<NPCData>(agent.Transform.Position, _searchRadius)
			.Where(e => e != agent)
			.OrderBy(e => agent.Transform.Position.DistanceSquaredTo(e.Transform.Position));

		foreach (var entity in candidates)
		{
			var data = entity.GetComponent<NPCData>();
			if (data == null || !IsCompatible(agentData, data, agent))
				continue;

			if (!ResourceReservationManager.Instance.IsAvailableFor(entity, agent))
				continue;

			return entity;
		}

		return null;
	}

	private static bool IsCompatible(NPCData seeker, NPCData candidate, Entity seekerEntity)
	{
		if (!candidate.IsAvailableForMate(seekerEntity))
			return false;

		if (candidate.Gender == seeker.Gender)
			return false;

		return true;
	}

	private void OnArrived() => _arrived = true;

	public override ActionStatus Update(Entity agent, float dt)
	{
		if (_failed || _mateTarget == null || _motor == null)
			return ActionStatus.Failed;

		_motor.Target = _mateTarget.Transform.Position;

		if (_arrived)
			return ActionStatus.Succeeded;

		float dist = agent.Transform.Position.DistanceTo(_mateTarget.Transform.Position);
		if (dist <= _reachDistance)
		{
			_arrived = true;
			return ActionStatus.Succeeded;
		}

		return ActionStatus.Running;
	}

	public override void Exit(Entity agent, ActionExitReason reason)
	{
		if (_motor != null)
		{
			_motor.OnTargetReached -= OnArrived;
			if (reason != ActionExitReason.Completed)
			{
				_motor.Target = null;
			}
		}

		if (_mateTarget != null && reason != ActionExitReason.Completed)
		{
			ResourceReservationManager.Instance.Release(_mateTarget, agent);
		}

		if (agent.TryGetComponent<NPCData>(out var data))
		{
			if (reason != ActionExitReason.Completed)
			{
				data.ClearActiveMate();
			}
		}
	}

	public override bool StillValid(Entity agent)
	{
		if (_failed || _mateTarget == null)
			return false;

		if (!agent.TryGetComponent<NPCData>(out var data))
			return false;

		return EvaluateGuardPeriodically(agent, () =>
		{
			if (_mateTarget == null || !_mateTarget.IsActive)
				return false;

			if (!IsCompatible(data, _mateTarget.GetComponent<NPCData>(), agent))
				return false;

			if (!ResourceReservationManager.Instance.IsReservedBy(_mateTarget, agent))
				return false;

			return true;
		});
	}

	public override void Fail(string reason)
	{
		GD.PushError($"GoToMate fail: {reason}");
		_failed = true;
	}
}

