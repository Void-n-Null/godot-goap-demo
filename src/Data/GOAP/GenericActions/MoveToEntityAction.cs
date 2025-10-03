using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Godot;

namespace Game.Data.GOAP.GenericActions;

/// <summary>
/// Generic action to move to an entity matching specified criteria.
/// Replaces GoToFoodAction, GoToTargetAction, and similar movement actions.
/// </summary>
public sealed class MoveToEntityAction : IAction, IRuntimeGuard
{
    private readonly EntityFinderConfig _finderConfig;
    private readonly float _reachDistance;
    private readonly string _actionName;
    
    private Entity _targetEntity;
    private NPCMotorComponent _motor;
    private bool _failed;
    private bool _arrived;

    public string Name => _actionName;

    public MoveToEntityAction(EntityFinderConfig finderConfig, float reachDistance = 64f, string actionName = "MoveToEntity")
    {
        _finderConfig = finderConfig;
        _reachDistance = reachDistance;
        _actionName = actionName;
    }

    public void Enter(Entity agent)
    {
        if (!agent.TryGetComponent<NPCMotorComponent>(out _motor))
        {
            Fail("Agent lacks NPCMotorComponent");
            return;
        }

        // Find target entity
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

        // Get nearest
        _targetEntity = candidates
            .OrderBy(e => agent.Transform.Position.DistanceTo(e.Transform.Position))
            .FirstOrDefault();

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

        GD.Print($"[{agent.Name}] {_actionName}: Moving to {_targetEntity.Name} at {_targetEntity.Transform.Position}");

        _motor.OnTargetReached += OnArrived;
        _motor.Target = _targetEntity.Transform.Position;
    }

    private void OnArrived() => _arrived = true;

    public ActionStatus Update(Entity agent, float dt)
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

    public void Exit(Entity agent, ActionExitReason reason)
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

    public bool StillValid(Entity agent)
    {
        if (_failed) return false;

        // Check if target still exists
        if (_targetEntity == null || !EntityManager.Instance.AllEntities.Contains(_targetEntity))
            return false;

        return true;
    }

    public void Fail(string reason)
    {
        GD.PushError($"{_actionName} fail: {reason}");
        _failed = true;
    }
}
