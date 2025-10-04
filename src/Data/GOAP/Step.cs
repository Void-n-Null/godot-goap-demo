using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.NPCActions;

namespace Game.Data.GOAP;

public class Step
{
    private readonly Func<IAction> _actionFactory;
    private readonly Func<State, double> _costFactory;
    public Dictionary<string, object> Preconditions { get; }
    public Dictionary<string, object> Effects { get; }

    public Step(Func<IAction> actionFactory, Dictionary<string, object> preconditions, Dictionary<string, object> effects, Func<State, double> costFactory = null)
    {
        _actionFactory = actionFactory ?? throw new ArgumentNullException(nameof(actionFactory));
        _costFactory = costFactory ?? (ctx => 1.0);
        Preconditions = new Dictionary<string, object>(preconditions ?? new Dictionary<string, object>());
        Effects = new Dictionary<string, object>(effects ?? new Dictionary<string, object>());
    }

    public IAction CreateAction() => _actionFactory();

    public double GetCost(State ctx) => _costFactory(ctx);

    public bool CanRun(State ctx)
    {
        foreach (var precond in Preconditions)
        {
            // Use direct TryGetValue instead of Facts property to avoid unnecessary view creation
            if (!ctx.TryGetValue(precond.Key, out var currentValue))
            {
                return false;
            }

            // For count facts, check >= instead of exact equality
            if (precond.Key.EndsWith("Count") && precond.Value is int requiredCount && currentValue is int actualCount)
            {
                if (actualCount < requiredCount)
                {
                    return false;
                }
            }
            else if (!currentValue.Equals(precond.Value))
            {
                return false;
            }
        }
        return true;
    }
}