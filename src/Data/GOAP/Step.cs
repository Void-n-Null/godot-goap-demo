using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.NPCActions;

namespace Game.Data.GOAP;

public class Step
{
    private readonly Func<IAction> _actionFactory;
    private readonly Func<State, double> _costFactory;
    
    public State Preconditions { get; }
    public List<(string Key, object Value)> Effects { get; }

    public Step(Func<IAction> actionFactory, State preconditions, List<(string, object)> effects, Func<State, double> costFactory = null)
    {
        _actionFactory = actionFactory ?? throw new ArgumentNullException(nameof(actionFactory));
        _costFactory = costFactory ?? (ctx => 1.0);
        Preconditions = preconditions ?? State.Empty();
        Effects = effects ?? new List<(string, object)>();
    }

    public IAction CreateAction() => _actionFactory();

    public double GetCost(State ctx) => _costFactory(ctx);

    public bool CanRun(State ctx)
    {
        return ctx.Satisfies(Preconditions);
    }
}