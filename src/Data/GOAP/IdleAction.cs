using Game.Data.Components;
using Game.Universe;
using Godot;
using System.Linq;

namespace Game.Data.GOAP;

public sealed class IdleAction : IAction
{
    private readonly float _duration;
    private float _timer;
    private NPCMotorComponent _motor;
    private bool _isWandering;
    private Vector2 _wanderTarget;

    public string Name => "Idle";

    public IdleAction(float durationSeconds = 1.0f)
    {
        _duration = durationSeconds;
    }

    public void Enter(Entity agent)
    {
        _timer = 0f;
        _isWandering = false;

        // 10% chance to wander
        if (GD.Randf() < 0.1f)
        {
            if (agent.TryGetComponent(out _motor))
            {
                _wanderTarget = PickWanderTarget(agent);
                _motor.Target = _wanderTarget;
                _isWandering = true;
            }
        }
    }

    private Vector2 PickWanderTarget(Entity agent)
    {
        var currentPos = agent.Transform.Position;

        // Find nearby NPCs within 2000 units
        var nearbyNPCs = ServiceLocator.EntityManager
            .QueryByComponent<NPCData>(currentPos, 2000f)
            .Where(e => e != agent)
            .ToList();

        // If there are nearby NPCs, bias towards staying near them
        if (nearbyNPCs.Count > 0)
        {
            // Pick a random nearby NPC
            var targetNPC = nearbyNPCs[GD.RandRange(0, nearbyNPCs.Count - 1)];
            var targetPos = targetNPC.Transform.Position;

            // Wander to a point near that NPC (within 500 units)
            var angle = GD.Randf() * Mathf.Tau;
            var distance = GD.Randf() * 500f;
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            return targetPos + offset;
        }
        else
        {
            // No one nearby, just wander randomly within 500 units
            var angle = GD.Randf() * Mathf.Tau;
            var distance = GD.Randf() * 500f;
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            return currentPos + offset;
        }
    }

    public ActionStatus Update(Entity agent, float dt)
    {
        _timer += dt;

        if (_isWandering)
        {
            // Check if we've reached the wander target or timed out
            if (_motor != null && _motor.Target == null)
            {
                // Motor cleared target, we've arrived
                return ActionStatus.Succeeded;
            }

            // Timeout after duration
            if (_timer >= _duration * 3f) // Give more time for wandering
            {
                if (_motor != null)
                {
                    _motor.Target = null;
                }
                return ActionStatus.Succeeded;
            }
        }
        else
        {
            // Just idling in place
            if (_timer >= _duration)
            {
                return ActionStatus.Succeeded;
            }
        }

        return ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason)
    {
        // Clean up motor target if we're wandering
        if (_isWandering && _motor != null)
        {
            _motor.Target = null;
        }
    }

    public void Fail(string reason)
    {
        GD.PushWarning($"IdleAction fail: {reason}");
    }
}

