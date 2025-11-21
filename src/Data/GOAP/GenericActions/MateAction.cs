using System;
using Game.Data.Components;
using Game.Data.Blueprints;
using Game.Universe;
using Game.Utils;
using Godot;
using Game.Data.GOAP;

namespace Game.Data.GOAP.GenericActions;

public sealed class MateAction(float interactionTime = 2.5f, float requiredDistance = 96f) : PeriodicGuardAction(0.25f)
{
	private readonly float _interactionTime = interactionTime;
	private readonly float _requiredDistance = requiredDistance;

	private Entity _partner;
	private float _timer;
	private bool _failed;
	private bool _completed;

	public override string Name => "Mate";

	public override void Enter(Entity agent)
	{
		if (!agent.TryGetComponent<NPCData>(out var data))
		{
			Fail("Missing NPCData");
			return;
		}

		_partner = data.GetActiveMateEntity();
		if (_partner == null)
		{
			Fail("No active mate");
			return;
		}

		if (!ResourceReservationManager.Instance.IsReservedBy(_partner, agent))
		{
			Fail("Mate was not reserved");
			return;
		}
	}

	public override ActionStatus Update(Entity agent, float dt)
	{
		if (_failed || _partner == null)
			return ActionStatus.Failed;

		if (_completed)
			return ActionStatus.Succeeded;

		if (!agent.TryGetComponent<NPCData>(out var agentData) ||
		    !IsValidPartner(agentData, _partner))
		{
			Fail("Partner invalid during mating");
			return ActionStatus.Failed;
		}

		float distance = agent.Transform.Position.DistanceTo(_partner.Transform.Position);
		if (distance > _requiredDistance)
		{
			Fail("Partner too far");
			return ActionStatus.Failed;
		}

		_timer += dt;
		if (_timer >= _interactionTime)
		{
			Complete(agent, agentData);
			return ActionStatus.Succeeded;
		}

		return ActionStatus.Running;
	}

	private void Complete(Entity agent, NPCData agentData)
	{
		if (_partner == null || !_partner.TryGetComponent<NPCData>(out var partnerData))
		{
			Fail("Partner missing at completion");
			return;
		}

		var midpoint = (agent.Transform.Position + _partner.Transform.Position) * 0.5f;

		SpawnHearts(midpoint);
		SpawnChild(midpoint);

		agentData.ApplyMateCooldown();
		partnerData.ApplyMateCooldown();

		ResourceReservationManager.Instance.Release(_partner, agent);
		partnerData.ClearActiveMate();
		agentData.ClearActiveMate();

		_completed = true;
	}

	private void SpawnHearts(Vector2 position)
	{
		for (int i = 0; i < 4; i++)
		{
			var offset = new Vector2(Utils.Random.NextFloat(-20f, 20f), Utils.Random.NextFloat(-10f, 10f));
			var heart = SpawnEntity.Now(Particles.Heart, position + offset);
			if (heart.TryGetComponent<SpriteParticleComponent>(out var particle))
			{
				var velocity = new Vector2(Utils.Random.NextFloat(-60f, 60f), Utils.Random.NextFloat(-120f, -60f));
				particle.SetVelocity(velocity);
			}
		}
	}

	private void SpawnChild(Vector2 position)
	{
		var spawnPos = position + new Vector2(Utils.Random.NextFloat(-40f, 40f), Utils.Random.NextFloat(-10f, 10f));
		var child = SpawnEntity.Now(NPC.Intelligent, spawnPos);
		if (child.TryGetComponent<NPCData>(out var data))
		{
			data.AgeGroup = NPCAgeGroup.Child;
			data.MatingDesire = 0f;
			data.ClearMateCooldown();
			data.Gender = Utils.Random.NextBool() ? NPCGender.Male : NPCGender.Female;

			if (child.TryGetComponent<VisualComponent>(out var visual))
			{
				var path = NPC.DetermineSpritePath(data);
				visual.SetSprite(path);
			}
		}
	}

	private static bool IsValidPartner(NPCData agentData, Entity partner)
	{
		if (partner == null || !partner.TryGetComponent<NPCData>(out var partnerData))
			return false;

		if (!partnerData.IsAvailableForMate(agentData.Entity))
			return false;

		if (partnerData.Gender == agentData.Gender)
			return false;

		return true;
	}

	public override void Exit(Entity agent, ActionExitReason reason)
	{
		if (reason != ActionExitReason.Completed && _partner != null)
		{
			ResourceReservationManager.Instance.Release(_partner, agent);
			if (_partner.TryGetComponent<NPCData>(out var partnerData) && partnerData.ActiveMateTargetId == agent.Id)
			{
				partnerData.ClearActiveMate();
			}
		}

		if (agent.TryGetComponent<NPCData>(out var data))
		{
			data.ClearActiveMate();
		}
	}

	public override bool StillValid(Entity agent)
	{
		if (_failed)
			return false;

		return EvaluateGuardPeriodically(agent, () =>
		{
			if (_partner == null || !_partner.IsActive)
				return false;

			return true;
		});
	}

	public override void Fail(string reason)
	{
		GD.PushError($"MateAction fail: {reason}");
		_failed = true;
	}
}

