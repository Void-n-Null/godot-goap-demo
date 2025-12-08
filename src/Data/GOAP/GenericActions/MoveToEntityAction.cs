using System;
using Game.Data.Components;
using Game.Universe;
using Godot;
using Game.Utils;

namespace Game.Data.GOAP.GenericActions;

/// <summary>
/// Generic action to move to an entity matching specified criteria.
/// Replaces GoToFoodAction, GoToTargetAction, and similar movement actions.
/// </summary>
public sealed class MoveToEntityAction(EntityFinderConfig finderConfig, float reachDistance = 64f, string actionName = "MoveToEntity") : PeriodicGuardAction(0.5f)
{
    private readonly EntityFinderConfig _finderConfig = finderConfig;
    private readonly float _reachDistance = reachDistance;
    private readonly float _reachDistanceSquared = reachDistance * reachDistance;
    private readonly string _actionName = actionName;
    
    private Entity _targetEntity;
    private NPCMotorComponent _motor;
    private bool _failed;
    private bool _arrived;

    public override string Name => _actionName;

    public override void Enter(Entity agent)
    {
        if (!agent.TryGetComponent(out _motor))
        {
            Fail("Agent lacks NPCMotorComponent");
            return;
        }

        // Use expanding search to find nearest entity without querying entire world
        _targetEntity = FindNearestTarget(agent);

        if (_targetEntity == null)
        {
            Fail($"No matching entity found for {_actionName}");
            return;
        }

        // Reserve if needed
        if (_finderConfig.ShouldReserve && !ResourceReservationManager.Instance.TryReserve(_targetEntity, agent))
        {
            Fail($"Failed to reserve target for {_actionName}");
            return;
        }

        LM.Info($"[{agent.Name}] {_actionName}: Moving to {_targetEntity.Name} at {_targetEntity.Transform.Position}");

        _motor.OnTargetReached += OnArrived;
        _motor.Target = _targetEntity.Transform.Position;
    }

    private Entity FindNearestTarget(Entity agent)
    {
        // Expanding search using SpatialIndex.FindNearest to avoid materializing large lists.
        float searchRadius = _finderConfig.SearchRadius;
        const int MAX_ATTEMPTS = 3;
        var agentPos = agent.Transform.Position;

        // Build predicate once to avoid delegate allocations per attempt.
        bool Filter(Entity e)
        {
            // Transform check first to avoid null deref
            if (e.Transform == null) return false;

            if (_finderConfig.Filter != null && !_finderConfig.Filter(e))
                return false;

            if (_finderConfig.RequireUnreserved && !ResourceReservationManager.Instance.IsAvailableFor(e, agent))
                return false;

            if (_finderConfig.RequireReservation && !ResourceReservationManager.Instance.IsReservedBy(e, agent))
                return false;

            return true;
        }

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            var candidate = ServiceLocator.EntityManager.SpatialPartition
                .FindNearest(agentPos, searchRadius, Filter);

            if (candidate != null)
                return candidate;

            searchRadius *= 2f; // expand search radius for next attempt
        }

        return null;
    }

    private void OnArrived() => _arrived = true;

    public override ActionStatus Update(Entity agent, float dt)
    {
        if (_failed || _targetEntity == null)
            return ActionStatus.Failed;

        if (_arrived)
            return ActionStatus.Succeeded;

        // Check distance manually as fallback
        var delta = agent.Transform.Position - _targetEntity.Transform.Position;
        var distanceSq = delta.LengthSquared();
        if (distanceSq <= _reachDistanceSquared)
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

                // Release reservation if we didn't complete
                if (_finderConfig.ShouldReserve && _targetEntity != null)
                {
                    ResourceReservationManager.Instance.Release(_targetEntity, agent);
                }
            }
        }
    }

    public override bool StillValid(Entity agent)
    {
        if (_failed) return false;
        if (_arrived) return true;
        if (_targetEntity == null || !_targetEntity.IsActive) return false;

        return EvaluateGuardPeriodically(agent, () =>
        {
            bool valid = true;

            if (_finderConfig.Filter != null)
            {
                try
                {
                    valid = _finderConfig.Filter(_targetEntity);
                }
                catch
                {
                    valid = false;
                }
            }

            if (valid)
            {
                if (_finderConfig.RequireUnreserved)
                {
                    valid = ResourceReservationManager.Instance.IsAvailableFor(_targetEntity, agent);
                }
                else if (_finderConfig.RequireReservation)
                {
                    valid = ResourceReservationManager.Instance.IsReservedBy(_targetEntity, agent);
                }
            }

            return valid;
        });
    }

    public override void Fail(string reason)
    {
        LM.Error($"{_actionName} fail: {reason}");
        _failed = true;
    }
}
