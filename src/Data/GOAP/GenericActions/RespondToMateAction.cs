using System;
using Game.Data.Components;
using Game.Universe;
using Game.Utils;
using Godot;

namespace Game.Data.GOAP.GenericActions;

public sealed class RespondToMateAction : PeriodicGuardAction
{
    private Entity _requester;
    private NPCMotorComponent _motor;
    private bool _completed;
    private bool _failed;
    private bool _accepted;

    public RespondToMateAction() : base(0.0f) { } // 0.0f = update every frame for precise tracking

    public override string Name => "RespondToMate";

    public override void Enter(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var data))
        {
            Fail("Missing NPCData");
            return;
        }

        if (data.IncomingMateRequestStatus != NPCData.MateRequestStatus.Pending)
        {
            Fail("No pending request");
            return;
        }

        _requester = ServiceLocator.EntityManager.GetEntityById(data.IncomingMateRequestFrom);
        if (_requester == null || !_requester.IsActive)
        {
            data.ClearMateRequest();
            Fail("Requester invalid");
            return;
        }

        // Decide whether to accept or reject
        // We use a slightly lower threshold for accepting than for seeking, 
        // because being asked is flattering (or just easier).
        float acceptThreshold = NPCData.MatingDesireThreshold * 0.8f;

        if (data.MatingDesire >= acceptThreshold && !data.IsOnMateCooldown)
        {
            // Accept!
            _accepted = true;
            data.AcceptMateRequest();
            data.SetActiveMate(_requester); // Lock in the partner

            // Move towards them
            if (agent.TryGetComponent(out _motor))
            {
                _motor.Target = _requester.Transform.Position;
            }

            LM.Info($"[RespondToMate] {agent.Name} ACCEPTED request from {_requester.Name}");
        }
        else
        {
            // Reject
            data.RejectMateRequest();
            LM.Info($"[RespondToMate] {agent.Name} REJECTED request from {_requester.Name} (Desire: {data.MatingDesire:F1})");
            _completed = true; // Action is done (we rejected)
        }
    }

    public override ActionStatus Update(Entity agent, float dt)
    {
        if (_failed) return ActionStatus.Failed;
        if (_completed) return ActionStatus.Succeeded;

        if (_accepted)
        {
            // If accepted, we are moving towards the requester
            if (_requester == null || !_requester.IsActive)
            {
                Fail("Requester vanished");
                return ActionStatus.Failed;
            }

            float dist = agent.Transform.Position.DistanceTo(_requester.Transform.Position);

            // Stop moving when we're close enough - let the male close the final distance
            // This prevents the infinite "dance" where both NPCs chase each other
            if (dist < 80f)
            {
                // Stop moving, we're close enough
                if (_motor != null)
                {
                    _motor.Target = null;
                }

                // Wait for male to reach us (he has 64f reach distance)
                if (dist < 64f)
                {
                    _completed = true;
                    return ActionStatus.Succeeded;
                }
            }
            else
            {
                // Still far away, keep moving towards him
                if (_motor != null)
                {
                    _motor.Target = _requester.Transform.Position;
                }
            }
        }

        return ActionStatus.Running;
    }

    public override void Exit(Entity agent, ActionExitReason reason)
    {
        if (_motor != null)
        {
            _motor.Target = null;
        }

        // If we failed or cancelled while pending, we should probably clear the request so we don't get stuck
        if (reason != ActionExitReason.Completed && agent.TryGetComponent<NPCData>(out var data))
        {
            if (data.IncomingMateRequestStatus == NPCData.MateRequestStatus.Pending)
            {
                // Default to reject if we are interrupted
                data.RejectMateRequest();
            }
        }
    }

    public override bool StillValid(Entity agent)
    {
        if (_failed) return false;
        return true;
    }

    public override void Fail(string reason)
    {
        LM.Error($"RespondToMate fail: {reason}");
        _failed = true;
    }
}
