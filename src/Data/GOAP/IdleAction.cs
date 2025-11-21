using Game.Data.Components;
using Godot;

namespace Game.Data.GOAP;

public sealed class IdleAction : IAction
{
    private readonly float _duration;
    private float _timer;

    public string Name => "Idle";

    public IdleAction(float durationSeconds = 1.0f)
    {
        _duration = durationSeconds;
    }

    public void Enter(Entity agent)
    {
        _timer = 0f;
    }

    public ActionStatus Update(Entity agent, float dt)
    {
        _timer += dt;
        if (_timer >= _duration)
        {
            return ActionStatus.Succeeded;
        }
        return ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason)
    {
        // nothing to clean up
    }

    public void Fail(string reason)
    {
        GD.PushWarning($"IdleAction fail: {reason}");
    }
}

