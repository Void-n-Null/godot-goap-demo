using Godot;
using Game.Utils;
using Game.Universe;

namespace Game.Data.GOAP;

public abstract class PeriodicGuardAction : IAction, IRuntimeGuard
{
    private readonly float _guardInterval;
    private float _lastGuardCheck;
    private bool _lastGuardResult = true;

    protected PeriodicGuardAction(float guardIntervalSeconds = 0.5f)
    {
        _guardInterval = guardIntervalSeconds;
    }

    protected bool EvaluateGuardPeriodically(Entity agent, System.Func<bool> guardCheck)
    {
        float currentTimeSeconds = FrameTime.TimeSeconds;
        if (FrameTime.FrameIndex == 0 && currentTimeSeconds <= 0f)
        {
            currentTimeSeconds = GameManager.Instance.CachedTimeMsec / 1000f;
        }

        if (currentTimeSeconds - _lastGuardCheck >= _guardInterval)
        {
            _lastGuardCheck = currentTimeSeconds;
            _lastGuardResult = guardCheck();
        }
        return _lastGuardResult;
    }

    public abstract string Name { get; }
    public abstract void Enter(Entity agent);
    public abstract ActionStatus Update(Entity agent, float dt);
    public abstract void Exit(Entity agent, ActionExitReason reason);
    public abstract void Fail(string reason);
    public abstract bool StillValid(Entity agent);
}

