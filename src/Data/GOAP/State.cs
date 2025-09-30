using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Data.GOAP;

public class State
{
    public IReadOnlyDictionary<string, object> Facts { get; }

    public State(Dictionary<string, object> facts)
    {
        Facts = new Dictionary<string, object>(facts ?? new Dictionary<string, object>());
    }

    public static State Empty() => new State(new Dictionary<string, object>());

    public State Clone()
    {
        return new State(new Dictionary<string, object>(Facts));
    }

    public bool Satisfies(State goal)
    {
        foreach (var goalFact in goal.Facts)
        {
            if (!Facts.TryGetValue(goalFact.Key, out var currentValue))
            {
                // For count facts, assume 0 if missing
                if (goalFact.Key.EndsWith("Count") && goalFact.Value is int)
                {
                    currentValue = 0;
                }
                else if (goalFact.Value is bool)
                {
                    // For boolean facts, assume false if missing
                    currentValue = false;
                }
                else
                {
                    return false;
                }
            }

            if (goalFact.Key.EndsWith("Count") && goalFact.Value is int goalCount && currentValue is int currentCount)
            {
                if (currentCount < goalCount) return false;
            }
            else if (!currentValue.Equals(goalFact.Value))
            {
                return false;
            }
        }
        return true;
    }

    // Equality and hash for planning (optional, for visited sets)
    public override bool Equals(object obj)
    {
        return obj is State other && Satisfies(other) && other.Satisfies(this);
    }

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var kvp in Facts.OrderBy(k => k.Key))
        {
            hash = hash * 23 + (kvp.Key?.GetHashCode() ?? 0);
            hash = hash * 23 + (kvp.Value?.GetHashCode() ?? 0);
        }
        return hash;
    }
}