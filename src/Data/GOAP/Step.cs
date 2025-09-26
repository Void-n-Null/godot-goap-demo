using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.NPCActions;

namespace Game.Data.GOAP;

public class Step
{
    private readonly Func<State, IAction> _actionFactory;
    private readonly Func<State, double> _costFactory;
    public Dictionary<string, object> Preconditions { get; }
    public Dictionary<string, object> Effects { get; }

    public Step(Func<State, IAction> actionFactory, Dictionary<string, object> preconditions, Dictionary<string, object> effects, Func<State, double> costFactory = null)
    {
        _actionFactory = actionFactory ?? throw new ArgumentNullException(nameof(actionFactory));
        _costFactory = costFactory ?? (ctx => 1.0);
        Preconditions = new Dictionary<string, object>(preconditions ?? new Dictionary<string, object>());
        Effects = new Dictionary<string, object>(effects ?? new Dictionary<string, object>());
    }

    public IAction CreateAction(State ctx) => _actionFactory(ctx);

    public double GetCost(State ctx) => _costFactory(ctx);

    public bool CanRun(State ctx)
    {
        foreach (var precond in Preconditions)
        {
            if (!ctx.Facts.TryGetValue(precond.Key, out var currentValue) || !currentValue.Equals(precond.Value))
            {
                return false;
            }
        }
        return true;
    }
}