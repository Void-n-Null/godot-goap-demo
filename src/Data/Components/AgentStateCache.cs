using Game.Data.GOAP;
using Game.Data.UtilityAI;
using Godot;

namespace Game.Data.Components;

#nullable enable

/// <summary>
/// Caches and rebuilds agent/world state, and tracks deltas for planning decisions.
/// </summary>
public sealed class AgentStateCache
{
    private readonly GoalFactRegistry _facts;
    private readonly ProximityScanner _proximityScanner;

    private State _cachedState = new State();
    private State _lastPlanningState = new State();
    private bool _dirty = true;
    private float _lastStateBuildTime = -1000f;

    private const float STATE_REBUILD_INTERVAL = 0.25f;
    private const float DIRTY_REBUILD_INTERVAL = 0.05f;

    public AgentStateCache(
        GoalFactRegistry facts,
        ProximityScanner proximityScanner)
    {
        _facts = facts;
        _proximityScanner = proximityScanner;
    }

    public State GetOrBuildState(float currentTime, Entity entity)
    {
        float interval = _dirty ? DIRTY_REBUILD_INTERVAL : STATE_REBUILD_INTERVAL;
        if ((currentTime - _lastStateBuildTime) >= interval)
        {
            BuildCurrentState(currentTime, entity);
        }

        return _cachedState;
    }

    public void MarkDirty() => _dirty = true;

    public void ForceImmediateRebuild() => _lastStateBuildTime = float.NegativeInfinity;

    public void CaptureLastPlanningState(State currentState) => _lastPlanningState = currentState.Clone();

    public bool HasWorldStateChangedSignificantly(State currentState)
    {
        var keyFacts = new[] {
            FactKeys.AgentCount(Tags.Stick),
            FactKeys.WorldCount(Tags.Stick),
            FactKeys.WorldHas(Tags.Stick),
            FactKeys.WorldCount(Tags.Tree),
            FactKeys.WorldHas(Tags.Tree),
            FactKeys.WorldCount(Tags.Food),
            FactKeys.WorldHas(Tags.Food),
            "Hunger",
            "IsSleepy"
        };

        foreach (var key in keyFacts)
        {
            bool currentHas = currentState.TryGet(key, out var currentVal);
            bool lastHas = _lastPlanningState.TryGet(key, out var lastVal);

            if (currentHas != lastHas)
            {
                return true;
            }

            if (currentHas && currentVal != lastVal)
            {
                return true;
            }
        }

        return false;
    }

    private void BuildCurrentState(float currentTime, Entity entity)
    {
        _cachedState.Clear();

        if (entity.TryGetComponent<NPCData>(out var npcData))
        {
            AddNPCFacts(_cachedState, npcData);
        }

        if (entity.TryGetComponent<TransformComponent2D>(out var agentTransform))
        {
            _proximityScanner.RebuildAndApply(_cachedState, agentTransform, entity, currentTime);
        }

        _lastStateBuildTime = currentTime;
        _dirty = false;
    }

    private void AddNPCFacts(State state, NPCData npcData)
    {
        for (int i = 0; i < _facts.TargetTags.Length; i++)
        {
            npcData.Resources.TryGetValue(_facts.TargetTags[i], out int agentCount);

            state.Set(_facts.AgentCountFactIds[i], agentCount);
            state.Set(_facts.AgentHasFactIds[i], agentCount > 0);
        }

        state.Set(_facts.HungerFactId, npcData.Hunger);
        state.Set(_facts.IsHungryFactId, npcData.Hunger > 30f);
        state.Set(_facts.SleepinessFactId, npcData.Sleepiness);
        state.Set(_facts.IsSleepyFactId, npcData.Sleepiness > 70f);
    }
}

