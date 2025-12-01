using System;
using System.Linq;
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
    private readonly string _actionName = actionName;
    
    private Entity _targetEntity;
    private NPCMotorComponent _motor;
    private bool _failed;
    private bool _arrived;

    public override string Name => _actionName;

    public override void Enter(Entity agent)
    {
        LM.Debug($"[{agent.Name}] {_actionName}: Enter called");
        
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
        // Expanding search: start with configured radius, expand up to 3 times if needed
        float searchRadius = _finderConfig.SearchRadius;
        const int MAX_ATTEMPTS = 3;
        var random = new System.Random();

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            var candidates = ServiceLocator.EntityManager
                .QueryByComponent<TransformComponent2D>(agent.Transform.Position, searchRadius)
                .Where(_finderConfig.Filter);

            // Apply reservation filters
            if (_finderConfig.RequireUnreserved)
            {
                candidates = candidates.Where(e => ResourceReservationManager.Instance.IsAvailableFor(e, agent));
            }
            else if (_finderConfig.RequireReservation)
            {
                candidates = candidates.Where(e => ResourceReservationManager.Instance.IsReservedBy(e, agent));
            }

            // Get nearest with small random offset to reduce contention
            // (prevents all agents from targeting the exact same tree/resource)
            var nearest = candidates
                .OrderBy(e => agent.Transform.Position.DistanceTo(e.Transform.Position) + (float)random.NextDouble() * 50f)
                .FirstOrDefault();

            if (nearest != null)
                return nearest;

            // Expand search radius for next attempt
            searchRadius *= 2f;
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
        var distance = agent.Transform.Position.DistanceTo(_targetEntity.Transform.Position);
        if (distance <= _reachDistance)
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

        return EvaluateGuardPeriodically(agent, () =>
        {
            // If current target is gone, try to find a new one instead of failing
            if (_targetEntity == null || !_targetEntity.IsActive)
            {
                var newTarget = FindNearestTarget(agent);
                if (newTarget != null)
                {
                    // Release old reservation if we had one
                    if (_targetEntity != null && _finderConfig.ShouldReserve)
                    {
                        ResourceReservationManager.Instance.Release(_targetEntity, agent);
                    }
                    
                    // Switch to new target
                    _targetEntity = newTarget;
                    
                    // Reserve new target
                    if (_finderConfig.ShouldReserve && !ResourceReservationManager.Instance.TryReserve(_targetEntity, agent))
                    {
                        LM.Warning($"[{agent.Name}] {_actionName}: Found new target but couldn't reserve it");
                        return false;
                    }
                    
                    // Update motor destination
                    if (_motor != null)
                    {
                        _motor.Target = _targetEntity.Transform.Position;
                    }
                    
                    LM.Info($"[{agent.Name}] {_actionName}: Original target gone, switching to {_targetEntity.Name}");
                    return true;
                }
                
                // No alternative target found - now we can fail
                LM.Warning($"[{agent.Name}] {_actionName}: Target gone and no alternatives found");
                return false;
            }

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
