using System.Collections.Generic;

namespace Game.Data.NPCActions;

public sealed class Plan
{
    private readonly Queue<IAction> _actions = new();
    private IAction _current;

    public Plan(IEnumerable<IAction> actions)
    {
        foreach (var a in actions) _actions.Enqueue(a);
    }

    public bool Tick(Entity actor, float dt)
    {
        if (_current == null)
        {
            if (_actions.Count == 0) return true; // plan done
            _current = _actions.Dequeue();
            _current.Enter(actor);
        }

        var status = _current.Update(actor, dt);
        if (status == ActionStatus.Running) return false;

        _current.Exit(actor,
            status == ActionStatus.Succeeded ? ActionExitReason.Completed : ActionExitReason.Failed);

        if (status == ActionStatus.Failed) return true; // plan failed
        _current = null;
        return _actions.Count == 0; // true if now empty
    }

    public void Cancel(Entity actor)
    {
        if (_current != null)
        {
            _current.Exit(actor, ActionExitReason.Cancelled);
            _current = null;
        }
        _actions.Clear();
    }
}
