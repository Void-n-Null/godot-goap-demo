
using System.Threading;
using Game.Data.Components;
using Godot;
using System.Linq;
using Game.Universe;

namespace Game.Data.GOAP;

public sealed class ChopTargetAction(TargetType target = TargetType.Tree) : IAction, IRuntimeGuard
{
    private readonly TargetType _target = target;
    private Entity _targetEntity;
    private bool _completed;
    private bool _failed;
    private float _timer;

    public void Enter(RuntimeContext ctx)
    {
        _timer = 0f;
        var agent = ctx.Agent;
        var agentTransform = agent.GetComponent<TransformComponent2D>();
        const float CHOP_RADIUS = 64f; // Must match proximity radius in BuildCurrentState

        // Find nearest live target that we have reserved (should be reserved by GoToTargetAction)
        _targetEntity = EntityManager.Instance.AllEntities.OfType<Entity>()
            .Where(e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == _target 
                && e.TryGetComponent<HealthComponent>(out var hc) && hc.IsAlive
                && e.TryGetComponent<TransformComponent2D>(out var tTransform)
                && ResourceReservationManager.Instance.IsReservedBy(e, agent)) // Must be reserved by us
            .OrderBy(e => agentTransform.Position.DistanceTo(e.GetComponent<TransformComponent2D>().Position))
            .FirstOrDefault();

        if (_targetEntity == null)
        {
            Fail($"No reserved live {_target} target found for ChopTargetAction (target may have been stolen or not reserved)");
            _failed = true;
            return;
        }

        // CRITICAL: Verify the target is actually close enough to chop
        var targetTransform = _targetEntity.GetComponent<TransformComponent2D>();
        float distance = agentTransform.Position.DistanceTo(targetTransform.Position);
        
        if (distance > CHOP_RADIUS)
        {
            Fail($"ChopTargetAction: {_target} {_targetEntity.Id} is too far ({distance:F1} > {CHOP_RADIUS}). Agent at {agentTransform.Position}, target at {targetTransform.Position}");
            _failed = true;
            return;
        }

        if (!_targetEntity.TryGetComponent<HealthComponent>(out _))
        {
            Fail($"Nearest {_target} {_targetEntity.Id} lacks HealthComponent; cannot chop");
            _failed = true;
            return;
        }
        
        GD.Print($"ChopTargetAction Enter for {_target} {_targetEntity.Id}: distance={distance:F1}, starting timer");

        _completed = false;
        _failed = false;
    }

    public ActionStatus Update(RuntimeContext ctx, float dt)
    {
        if (_failed || _targetEntity == null) 
        {
            Fail("ChopTargetAction failed: target missing");
            return ActionStatus.Failed;
        }

        _timer += dt;
        
        if (_timer >= 3f)
        {
            // Kill the tree - it will spawn sticks automatically via HealthComponent.OnDeath()
            _targetEntity.GetComponent<HealthComponent>().Kill();
            
            // Release the reservation (tree is destroyed)
            ResourceReservationManager.Instance.Release(_targetEntity, ctx.Agent);
            
            _completed = true;
            GD.Print($"Chopped down {_target} {_targetEntity.Id} after {_timer:F1}s (sticks spawned by tree's death handler)");
        }

        return _completed ? ActionStatus.Succeeded : ActionStatus.Running;
    }

    public void Exit(RuntimeContext ctx, ActionExitReason reason)
    {
        if (reason != ActionExitReason.Completed) 
        {
            GD.Print("ChopTargetAction canceled or failed, stopping chop");
        }
    }

    public bool StillValid(RuntimeContext ctx)
    {
        if (_failed) return false;
        
        // Verify target still exists and is alive
        if (_targetEntity == null || !_targetEntity.TryGetComponent<HealthComponent>(out var health) || !health.IsAlive)
        {
            return false;
        }
        
        // Verify agent is still close enough to the target
        const float CHOP_RADIUS = 64f;
        var agentTransform = ctx.Agent.GetComponent<TransformComponent2D>();
        var targetTransform = _targetEntity.GetComponent<TransformComponent2D>();
        float distance = agentTransform.Position.DistanceTo(targetTransform.Position);
        
        if (distance > CHOP_RADIUS)
        {
            GD.Print($"ChopTargetAction no longer valid: agent moved too far from target ({distance:F1} > {CHOP_RADIUS})");
            return false;
        }
        
        return true;
    }

    public void Fail(string reason)
    {
        GD.PushError($"ChopTargetAction fail: {reason}");
        _failed = true;
    }
}
