
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

    public void Enter(State ctx)
    {
        _timer = 0f;
        var agent = ctx.Agent;
        var agentTransform = agent.GetComponent<TransformComponent2D>();

        // Find nearest live target using all entities to avoid spatial issues
        _targetEntity = EntityManager.Instance.AllEntities.OfType<Entity>()
            .Where(e => e.TryGetComponent<TargetComponent>(out var tc) && tc.Target == _target 
                && e.TryGetComponent<HealthComponent>(out var hc) && hc.IsAlive
                && e.TryGetComponent<TransformComponent2D>(out var tTransform))
            .OrderBy(e => agentTransform.Position.DistanceTo(e.GetComponent<TransformComponent2D>().Position))
            .FirstOrDefault();

        if (_targetEntity == null)
        {
            Fail($"No live {_target} target found for ChopTargetAction");
            _failed = true;
            return;
        }

        if (!_targetEntity.TryGetComponent<HealthComponent>(out _))
        {
            Fail($"Nearest {_target} {_targetEntity.Id} lacks HealthComponent; cannot chop");
            _failed = true;
            return;
        }
        GD.Print($"ChopTargetAction Enter for {_target} {_targetEntity.Id}: health found, starting timer");

        _completed = false;
        _failed = false;
    }

    public ActionStatus Update(State ctx, float dt)
    {
        if (_failed || _targetEntity == null) 
        {
            Fail("ChopTargetAction failed: target missing");
            return ActionStatus.Failed;
        }

        _timer += dt;
        if (_timer >= 3f)
        {
            _targetEntity.GetComponent<HealthComponent>().Kill();
            _completed = true;
            GD.Print($"Chopped down {_target} {_targetEntity.Id} after {_timer:F1}s");
        }

        return _completed ? ActionStatus.Succeeded : ActionStatus.Running;
    }

    public void Exit(State ctx, ActionExitReason reason)
    {
        if (reason != ActionExitReason.Completed) 
        {
            GD.Print("ChopTargetAction canceled or failed, stopping chop");
        }
    }

    public bool StillValid(State ctx)
    {
        if (_failed) return false;
        // Check if any live target exists
        return EntityManager.Instance.AllEntities.OfType<Entity>().Any(e => e.TryGetComponent<TargetComponent>(out var tc) 
            && tc.Target == _target && e.TryGetComponent<HealthComponent>(out var health) && health.CurrentHealth > 0);
    }

    public void Fail(string reason)
    {
        GD.PushError($"ChopTargetAction fail: {reason}");
        _failed = true;
    }
}
