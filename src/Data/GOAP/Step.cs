using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.NPCActions;

namespace Game.Data.GOAP;

public class Step
{
    private readonly Func<IAction> _actionFactory;
    private readonly Func<State, double> _costFactory;

    public string Name { get; }
    public State Preconditions { get; }
    public List<(string Key, object Value)> Effects { get; }
    
    /// <summary>
    /// Pre-resolved effect IDs to avoid string lookups during planning hot paths.
    /// </summary>
    public KeyValuePair<int, object>[] CompiledEffects { get; }

    public Step(string name, Func<IAction> actionFactory, State preconditions, List<(string, object)> effects, Func<State, double> costFactory = null)
    {
        Name = name;
        _actionFactory = actionFactory ?? throw new ArgumentNullException(nameof(actionFactory));
        _costFactory = costFactory ?? (ctx => 1.0);
        Preconditions = preconditions ?? State.Empty();
        Effects = effects ?? new List<(string, object)>();

        // Pre-compile effects into ID-based array for faster iteration in planner
        if (Effects.Count > 0)
        {
            CompiledEffects = new KeyValuePair<int, object>[Effects.Count];
            for (int i = 0; i < Effects.Count; i++)
            {
                var (key, value) = Effects[i];
                int id = FactRegistry.GetId(key);
                CompiledEffects[i] = new KeyValuePair<int, object>(id, value);
            }
        }
        else
        {
            CompiledEffects = Array.Empty<KeyValuePair<int, object>>();
        }
    }

    public IAction CreateAction() => _actionFactory();

    public double GetCost(State ctx) => _costFactory(ctx);

    public bool CanRun(State ctx)
    {
        return ctx.Satisfies(Preconditions);
    }
}
