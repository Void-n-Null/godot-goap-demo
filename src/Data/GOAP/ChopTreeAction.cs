using System;
using System.Threading;
using Game.Data;
using Game.Data.Components;
using Game.Data.GOAP;
using Game.Universe;
using Godot;

namespace Game.Data.GOAP;

public sealed class ChopTreeAction(Guid treeId) : IAction, IRuntimeGuard
{
    private readonly Guid _treeId = treeId;
    private Entity _treeEntity;
    private bool _completed;
    private bool _failed;
    private CancellationTokenSource _cts;
    private int _hitCount;

    public void Enter(State ctx)
    {
        _cts = new CancellationTokenSource();
        _treeEntity = ctx.World.EntityManager.GetEntityById(_treeId);
        if (_treeEntity == null)
        {
            Fail($"Tree entity {_treeId} not found for ChopTreeAction");
            _failed = true;
            _cts.Cancel();
            return;
        }

        if (!_treeEntity.TryGetComponent<HealthComponent>(out _))
        {
            Fail($"Tree {_treeId} lacks HealthComponent; cannot chop");
            _failed = true;
            _cts.Cancel();
            return;
        }
        GD.Print($"ChopTreeAction Enter for tree {_treeId}: health found, scheduling hits");

        _hitCount = 0;
        _completed = false;
        _failed = false;

        // Schedule hits every second for 5 seconds
        for (int i = 1; i <= 5; i++)
        {
            TaskScheduler.Instance.ScheduleSeconds(() =>
            {
                if (_cts.Token.IsCancellationRequested || _failed || _completed) return;
                _hitCount++;
                GD.Print("Hitting Tree");
                if (_hitCount == 5)
                {
                    // Final hit: complete chop
                    if (_treeEntity.TryGetComponent<HealthComponent>(out var health))
                    {
                        health.Kill();
                        GD.Print($"Chopped down tree {_treeId}");
                    }
                    else
                    {
                        Fail($"Tree {_treeId} no longer has HealthComponent during chop");
                    }
                    _completed = true;
                }
            }, i, _cts.Token);
        }
    }

    public ActionStatus Update(State ctx, float dt)
    {
        if (_failed || _treeEntity == null || _cts.Token.IsCancellationRequested) 
        {
            Fail("ChopTreeAction failed: tree missing or canceled");
            return ActionStatus.Failed;
        }

        return _completed ? ActionStatus.Succeeded : ActionStatus.Running;
    }

    public void Exit(State ctx, ActionExitReason reason)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        if (reason != ActionExitReason.Completed) 
        {
            GD.Print("ChopTreeAction canceled or failed, stopping schedule");
        }
    }

    public bool StillValid(State ctx)
    {
        if (_failed || _cts?.Token.IsCancellationRequested == true) return false;
        _treeEntity = ctx.World.EntityManager.GetEntityById(_treeId); // Refresh
        return _treeEntity != null && _treeEntity.TryGetComponent<HealthComponent>(out var health) && health.CurrentHealth > 0;
    }

    public void Fail(string reason)
    {
        GD.PushError($"ChopTreeAction fail: {reason}");
        _failed = true;
        _cts?.Cancel();
    }
}
