using System;
using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Godot;

namespace Game.Data.GOAP.GenericActions;

/// <summary>
/// Generic action for timed interactions with entities (pickup, chop, consume, etc).
/// Replaces PickUpTargetAction, ChopTargetAction, ConsumeFoodAction, and similar actions.
/// </summary>
public sealed class TimedInteractionAction : IAction, IRuntimeGuard
{
    private readonly EntityFinderConfig _finderConfig;
    private readonly float _interactionTime;
    private readonly InteractionEffectConfig _effectConfig;
    private readonly string _actionName;
    
    private Entity _targetEntity;
    private float _timer;
    private bool _failed;
    private bool _completed;

    public string Name => $"{_actionName} {_targetEntity?.Name}";

    public TimedInteractionAction(
        EntityFinderConfig finderConfig,
        float interactionTime,
        InteractionEffectConfig effectConfig,
        string actionName = "TimedInteraction")
    {
        _finderConfig = finderConfig;
        _interactionTime = interactionTime;
        _effectConfig = effectConfig;
        _actionName = actionName;
    }

    public void Enter(Entity agent)
    {
        _timer = 0f;
        _completed = false;

        // Use smaller search radius for interactions (should be nearby from movement step)
        _targetEntity = FindNearestTarget(agent);

        if (_targetEntity == null)
        {
            Fail($"No matching entity found for {_actionName}");
            return;
        }

        // ✅ FIXED: Reserve if needed (prevents race conditions)
        if (_finderConfig.ShouldReserve && !ResourceReservationManager.Instance.TryReserve(_targetEntity, agent))
        {
            Fail($"Failed to reserve target for {_actionName}");
            return;
        }

        GD.Print($"[{agent.Name}] {_actionName}: Starting interaction with {_targetEntity.Name} (duration: {_interactionTime}s)");
    }

    private Entity FindNearestTarget(Entity agent)
    {
        // Interactions should find nearby targets (agent already moved close via movement step)
        var candidates = ServiceLocator.EntityManager
            .QueryByComponent<TransformComponent2D>(agent.Transform.Position, _finderConfig.SearchRadius)
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
        // (prevents all agents from targeting the exact same stick)
        var random = new Random();
        return candidates
            .OrderBy(e => agent.Transform.Position.DistanceTo(e.Transform.Position) + (float)random.NextDouble() * 10f)
            .FirstOrDefault();
    }

    public ActionStatus Update(Entity agent, float dt)
    {
        if (_failed || _targetEntity == null)
            return ActionStatus.Failed;

        if (_completed)
            return ActionStatus.Succeeded;

        _timer += dt;

        if (_timer >= _interactionTime)
        {
            // Execute effect
            _effectConfig.OnComplete?.Invoke(agent, _targetEntity);

            // Handle cleanup
            if (_effectConfig.ReleaseReservationOnComplete)
            {
                ResourceReservationManager.Instance.Release(_targetEntity, agent);
            }

            if (_effectConfig.DestroyTargetOnComplete)
            {
                ServiceLocator.EntityManager.UnregisterEntity(_targetEntity);
                _targetEntity.Destroy();
            }

            _completed = true;
            GD.Print($"{_actionName} completed after {_timer:F2}s");
            return ActionStatus.Succeeded;
        }

        return ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason)
    {
        if (reason != ActionExitReason.Completed)
        {
            GD.Print($"{_actionName} canceled or failed before completion");
            
            // ✅ FIXED: Only release if WE created the reservation (ownership model)
            // If we just verified someone else's reservation (RequireReservation=true),
            // they are responsible for cleanup
            if (_targetEntity != null && _finderConfig.ShouldReserve)
            {
                ResourceReservationManager.Instance.Release(_targetEntity, agent);
                GD.Print($"[Reservation Cleanup] Released {_targetEntity.Name} due to {reason}");
            }
        }
    }

    public bool StillValid(Entity agent)
    {
        if (_failed || _targetEntity == null)
            return false;

        // Use IsActive instead of expensive linear search through all entities
        return _targetEntity.IsActive;
    }

    public void Fail(string reason)
    {
        GD.PushError($"{_actionName} fail: {reason}");
        _failed = true;
    }
}
