using Godot;
using System;
using System.Collections.Generic;
using Game.Data;
using Game.Data.GOAP;
using Game.Data.Components;
using Game.Universe;
using System.Threading.Tasks;
using Game.Data.UtilityAI;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Executes plans to achieve goals. Fires events to notify selector of status changes.
/// </summary>
public class AIGoalExecutor : IActiveComponent
{
    public Entity Entity { get; set; }

    private Plan _currentPlan;
    private Task<Plan> _planningTask;
    public bool PlanningInProgress { get; private set; }
    private IUtilityGoal _currentGoal;

    private State _cachedState;
    private State _lastPlanningState = new State();
    private bool _stateDirty = true;
    private float _lastStateBuildTime = -1000f;

    private float _lastPlanningTime;
    private const float MIN_PLANNING_INTERVAL = 0.1f;
    private bool _forceReplan;

    private int _consecutiveFailures;
    private const int MAX_IMMEDIATE_RETRIES = 2;

    private static readonly Tag[] _cachedTargetTags = Tags.TargetTags;
    private static readonly string[] _cachedTargetTagStrings;
    private static readonly int[] _nearFactIds;
    private static readonly int[] _worldCountFactIds;
    private static readonly int[] _worldHasFactIds;
    private static readonly int[] _agentCountFactIds;
    private static readonly int[] _agentHasFactIds;
    private static readonly int[] _distanceFactIds;
    private static readonly int HungerFactId;
    private static readonly int IsHungryFactId;
    private static readonly int SleepinessFactId;
    private static readonly int IsSleepyFactId;

    private bool[] _proximityNear;
    private int[] _availabilityCounts;
    private float[] _nearestDistances;
    private bool _worldEventsSubscribed;
    private ulong _lastProximityQueryFrame;
    private Vector2 _lastProximityQueryPosition;
    private bool _proximityRefreshPending;

    private float _lastProximityUpdate = -PROXIMITY_UPDATE_INTERVAL;
    private const float PROXIMITY_UPDATE_INTERVAL = 0.25f;
    private const float STATE_REBUILD_INTERVAL = 0.25f;
    private const float DIRTY_REBUILD_INTERVAL = 0.05f;
    private const float WORLD_EVENT_RADIUS = 1000f;
    private const float WORLD_EVENT_RADIUS_SQ = WORLD_EVENT_RADIUS * WORLD_EVENT_RADIUS;
    private const int MAX_PROXIMITY_RESULTS = 256;
    private const int MAX_AVAILABLE_PER_TYPE = 6;
    private static readonly bool PROFILE_BUILD_STATE = false;

    static AIGoalExecutor()
    {
        _cachedTargetTagStrings = new string[_cachedTargetTags.Length];
        _nearFactIds = new int[_cachedTargetTags.Length];
        _worldCountFactIds = new int[_cachedTargetTags.Length];
        _worldHasFactIds = new int[_cachedTargetTags.Length];
        _agentCountFactIds = new int[_cachedTargetTags.Length];
        _agentHasFactIds = new int[_cachedTargetTags.Length];
        _distanceFactIds = new int[_cachedTargetTags.Length];
        HungerFactId = FactRegistry.GetId("Hunger");
        IsHungryFactId = FactRegistry.GetId("IsHungry");
        SleepinessFactId = FactRegistry.GetId("Sleepiness");
        IsSleepyFactId = FactRegistry.GetId("IsSleepy");

        for (int i = 0; i < _cachedTargetTags.Length; i++)
        {
            string tagName = _cachedTargetTags[i].ToString();
            _cachedTargetTagStrings[i] = tagName;
            var tag = _cachedTargetTags[i];
            _nearFactIds[i] = FactRegistry.GetId(FactKeys.NearTarget(tag));
            _worldCountFactIds[i] = FactRegistry.GetId(FactKeys.WorldCount(tag));
            _worldHasFactIds[i] = FactRegistry.GetId(FactKeys.WorldHas(tag));
            _agentCountFactIds[i] = FactRegistry.GetId(FactKeys.AgentCount(tag));
            _agentHasFactIds[i] = FactRegistry.GetId(FactKeys.AgentHas(tag));
            _distanceFactIds[i] = FactRegistry.GetId($"Distance_To_{tag}");
        }
    }

    public event Action<IUtilityGoal> OnCannotPlan;
    public event Action<IUtilityGoal> OnPlanExecutionFailed;
    public event Action OnPlanSucceeded;
    public event Action OnGoalSatisfied;
    public event Action OnNeedNewGoal;

    public void SetGoal(IUtilityGoal goal)
    {
        if (_currentGoal != goal)
        {
            var oldGoal = _currentGoal?.Name ?? "None";
            var newGoal = goal?.Name ?? "None";
            var hadPlan = _currentPlan != null && !_currentPlan.IsComplete;
            LM.Debug($"[{Entity?.Name}] SetGoal: {oldGoal} -> {newGoal} (had active plan: {hadPlan})");
            
            _currentGoal = goal;
            _currentPlan = null;
            _stateDirty = true;
            _forceReplan = true; // New goal requires new plan immediately
        }
    }

    public void CancelCurrentPlan()
    {
        if (_currentPlan != null && !_currentPlan.IsComplete)
        {
            _currentPlan.Cancel(Entity);
            _currentPlan = null;
            _stateDirty = true;
            _forceReplan = true;
        }
    }

    public void Update(double delta)
    {
        float currentTime = GameManager.Instance.CachedTimeMsec / 1000.0f;

        // Watchdog: If we have no plan and aren't planning for too long, force a kick
        if (_currentPlan == null && !PlanningInProgress && _currentGoal != null)
        {
            if (currentTime - _lastPlanningTime > 2.0f) // 2 seconds stuck?
            {
                LM.Warning($"[{Entity.Name}] Watchdog: Stuck without plan for 2s. Forcing replan.");
                _forceReplan = true;
            }
        }

        var currentState = GetOrBuildState(currentTime);

        // Handle async planning completion
        if (PlanningInProgress && _planningTask != null)
        {
            if (_planningTask.IsCompletedSuccessfully)
            {
                var newPlan = _planningTask.Result;
                PlanningInProgress = false;
                _planningTask = null;

                // CRITICAL FIX: Don't overwrite an active plan!
                // If we already have a running plan, discard the new one.
                // This prevents race conditions where a planning task started earlier
                // completes and overwrites the current active plan.
                if (_currentPlan != null && !_currentPlan.IsComplete)
                {
                    LM.Debug($"[{Entity.Name}] Discarding completed plan - already have active plan");
                    // Don't use the new plan, keep executing current one
                }
                else if (newPlan != null && newPlan.Steps.Count > 0)
                {
                    _currentPlan = newPlan;
                    LM.Info($"[{Entity.Name}] New plan for '{_currentGoal?.Name}': {_currentPlan.Steps.Count} steps");
                    _consecutiveFailures = 0;
                }
                else
                {
                    string nameFirstWord = Entity.Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] ?? "Entity";
                    string idFirst6 = Entity.Id.ToString().Length >= 6 ? Entity.Id.ToString().Substring(0, 6) : Entity.Id.ToString();
                    LM.Warning($"[{nameFirstWord} {idFirst6}] Cannot plan for '{_currentGoal?.Name}' - no valid path to goal");
                    var failedGoal = _currentGoal;
                    _currentGoal = null;
                    _currentPlan = null;
                    _consecutiveFailures = 0;
                    _stateDirty = true;
                    OnCannotPlan?.Invoke(failedGoal);
                    return;
                }
            }
            else if (_planningTask.IsFaulted)
            {
                string nameFirstWord = Entity.Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] ?? "Entity";
                string idFirst6 = Entity.Id.ToString().Length >= 6 ? Entity.Id.ToString().Substring(0, 6) : Entity.Id.ToString();
                LM.Error($"[{nameFirstWord} {idFirst6}] Planning error for '{_currentGoal?.Name}': {_planningTask.Exception?.Message}");
                var failedGoal = _currentGoal;
                _currentGoal = null;
                PlanningInProgress = false;
                _planningTask = null;
                _stateDirty = true;
                OnCannotPlan?.Invoke(failedGoal);
                return;
            }
        }

        if (_currentGoal != null && _currentGoal.IsSatisfied(Entity))
        {
            LM.Info($"[{Entity.Name}] Goal '{_currentGoal.Name}' satisfied, clearing plan and goal");
            _currentPlan = null;
            _currentGoal = null;
            OnGoalSatisfied?.Invoke();
            return;
        }

        if (_currentGoal == null)
        {
            OnNeedNewGoal?.Invoke();
            return;
        }

        if ((_currentPlan == null || _currentPlan.IsComplete) && _currentGoal != null && !PlanningInProgress)
        {
            if (_currentPlan?.Succeeded == true)
            {
                _currentPlan = null;
            }

            float planningTime = GameManager.Instance.CachedTimeMsec / 1000.0f;
            bool shouldPlan = _forceReplan;

            if (!shouldPlan)
            {
                if (planningTime - _lastPlanningTime < MIN_PLANNING_INTERVAL)
                {
                    return;
                }
                shouldPlan = HasWorldStateChangedSignificantly(currentState);
            }

            if (shouldPlan)
            {
                PlanningInProgress = true;

                // Clone current state to last planning state
                _lastPlanningState = currentState.Clone();

                _lastPlanningTime = planningTime;
                _forceReplan = false;

                var goalState = _currentGoal.GetGoalState(Entity);
                LM.Debug($"[{Entity.Name}] Starting planning task for '{_currentGoal?.Name}'");
                // Need to pass a clone because planner might mutate it during search (or A* node expansion)
                _planningTask = AdvancedGoalPlanner.ForwardPlanAsync(currentState.Clone(), goalState);
            }
        }

        if (_currentPlan != null && !_currentPlan.IsComplete)
        {
            Func<Entity, bool> goalChecker = null;
            if (_currentGoal != null)
            {
                goalChecker = (agent) =>
                {
                    try
                    {
                        return _currentGoal.IsSatisfied(agent);
                    }
                    catch (Exception ex)
                    {
                        LM.Error($"Goal satisfaction check failed: {ex.Message}");
                        return false;
                    }
                };
            }

            var tickResult = _currentPlan.Tick(Entity, (float)delta, goalChecker);

            if (tickResult == PlanTickResult.Succeeded)
            {
                _currentPlan = null;
                _stateDirty = true;
                _consecutiveFailures = 0;
                _forceReplan = true; // Always replan immediately after success (e.g. for continuous goals like Idle)
                OnPlanSucceeded?.Invoke();
            }
            else if (tickResult == PlanTickResult.Failed)
            {
                LM.Warning($"[{Entity.Name}] Plan execution failed for '{_currentGoal?.Name}'");
                var failedGoal = _currentGoal;
                _currentPlan = null;
                _stateDirty = true;
                _consecutiveFailures++;

                _forceReplan = true;
                if (_consecutiveFailures > MAX_IMMEDIATE_RETRIES)
                {
                    LM.Warning($"[{Entity.Name}] Too many consecutive failures ({_consecutiveFailures}), waiting before retry");
                }

                GlobalWorldStateManager.Instance.ForceRefresh();
                OnPlanExecutionFailed?.Invoke(failedGoal);
            }
            // If tickResult == PlanTickResult.Running, continue executing next frame
        }
    }

    public void OnStart()
    {
        _cachedState ??= new State();
        _proximityNear ??= new bool[_cachedTargetTags.Length];
        _availabilityCounts ??= new int[_cachedTargetTags.Length];
        _nearestDistances ??= new float[_cachedTargetTags.Length];
        SubscribeToWorldEvents();
    }

    public void OnPostAttached()
    {
    }

    public void OnDetached()
    {
        if (_worldEventsSubscribed)
        {
            WorldEventBus.Instance.EntitySpawned -= OnWorldTargetEntityChanged;
            WorldEventBus.Instance.EntityDespawned -= OnWorldTargetEntityChanged;
            _worldEventsSubscribed = false;
        }
    }

    private bool HasWorldStateChangedSignificantly(State currentState)
    {
        // Compare currentState with _lastPlanningState
        // We can iterate key facts we care about.

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

    private State GetOrBuildState(float currentTime)
    {
        if (_cachedState == null)
        {
            _cachedState = new State();
            _stateDirty = true;
        }

        float interval = _stateDirty ? DIRTY_REBUILD_INTERVAL : STATE_REBUILD_INTERVAL;
        if ((currentTime - _lastStateBuildTime) >= interval)
        {
            BuildCurrentState(currentTime);
        }

        return _cachedState;
    }

    private void SubscribeToWorldEvents()
    {
        if (_worldEventsSubscribed) return;
        WorldEventBus.Instance.EntitySpawned += OnWorldTargetEntityChanged;
        WorldEventBus.Instance.EntityDespawned += OnWorldTargetEntityChanged;
        _worldEventsSubscribed = true;
    }

    private void OnWorldTargetEntityChanged(Entity entity)
    {
        if (entity == null) return;
        // Fast check: does entity have any target tag?
        bool hasTargetTag = false;
        foreach (var tag in _cachedTargetTags)
        {
            if (entity.HasTag(tag))
            {
                hasTargetTag = true;
                break;
            }
        }
        if (!hasTargetTag) return;

        // Ignore far away events so one new campfire doesn't reset the entire population
        if (Entity.TryGetComponent<TransformComponent2D>(out var selfTransform) &&
            entity.TryGetComponent<TransformComponent2D>(out var eventTransform))
        {
            if (selfTransform.Position.DistanceSquaredTo(eventTransform.Position) > WORLD_EVENT_RADIUS_SQ)
            {
                return;
            }
        }

        // Find which target tag this entity has (for goal relevance check)
        Tag entityTag = default;
        foreach (var tag in _cachedTargetTags)
        {
            if (entity.HasTag(tag))
            {
                entityTag = tag;
                break;
            }
        }

        var currentGoalState = _currentGoal?.GetGoalState(Entity);
        if (currentGoalState != null && !GoalReferencesTag(currentGoalState, entityTag))
        {
            return;
        }

        _stateDirty = true;
        _lastStateBuildTime = float.NegativeInfinity; // force immediate rebuild next tick
        _proximityRefreshPending = true;
        
        // DON'T cancel the current plan here! The IRuntimeGuard.StillValid() check in Plan.Tick
        // will properly detect when THIS agent's specific target is gone.
        // Cancelling here would cause ALL agents to give up when ANY food entity despawns,
        // even if they were targeting different food.
        //
        // Only set _forceReplan if we have NO current plan, so we can find a new target.
        if (_currentPlan == null || _currentPlan.IsComplete)
        {
            _forceReplan = true;
        }
    }

    private bool GoalReferencesTag(State goalState, Tag tag)
    {
        if (_currentGoal is IUtilityGoalTagInterest interest && interest.IsTargetTagRelevant(tag))
        {
            return true;
        }

        var nearKey = FactKeys.NearTarget(tag);
        var worldHasKey = FactKeys.WorldHas(tag);
        var worldCountKey = FactKeys.WorldCount(tag);

        return goalState.TryGet(nearKey, out _) ||
               goalState.TryGet(worldHasKey, out _) ||
               goalState.TryGet(worldCountKey, out _);
    }

    private void BuildCurrentState(float currentTime)
    {
        ulong startTicks = PROFILE_BUILD_STATE ? GameManager.Instance.CachedTimeMsec : 0UL;

        _cachedState.Clear();

        if (Entity.TryGetComponent<NPCData>(out var npcData))
        {
            AddNPCFacts(_cachedState, npcData);
        }

        // Force proximity update when rebuilding state to ensure freshness
        if (Entity.TryGetComponent<TransformComponent2D>(out var agentTransform))
        {
            UpdateProximityAndAvailability(agentTransform, Entity);
            _lastProximityUpdate = currentTime;
            _lastProximityQueryFrame = FrameTime.FrameIndex;
            _lastProximityQueryPosition = agentTransform.Position;
            _proximityRefreshPending = false;
        }

        ApplyProximityFacts(_cachedState);

        _lastStateBuildTime = currentTime;
        _stateDirty = false;

        if (PROFILE_BUILD_STATE)
        {
            var elapsed = GameManager.Instance.CachedTimeMsec - startTicks;
            LM.Debug($"[AIGoalExecutor] BuildCurrentState ({Entity.Name}) took {elapsed} ms");
        }
    }

    private void UpdateProximityAndAvailability(TransformComponent2D agentTransform, Entity agent)
    {
        var agentPos = agentTransform.Position;
        const float searchRadius = 5000f;

        Array.Fill(_proximityNear, false);
        Array.Clear(_availabilityCounts, 0, _availabilityCounts.Length);
        for (int i = 0; i < _nearestDistances.Length; i++)
        {
            _nearestDistances[i] = float.MaxValue;
        }

        // Query all entities and filter by tags
        var nearbyEntities = Universe.EntityManager.Instance?.SpatialPartition?.QueryCircle(
            agentPos,
            searchRadius,
            e => HasAnyTargetTag(e),
            MAX_PROXIMITY_RESULTS);

        if (nearbyEntities != null)
        {
            int tagCount = _cachedTargetTags.Length;
            const float nearDistSq = 64f * 64f;

            foreach (var entity in nearbyEntities)
            {
                // Find which tag index this entity matches
                int tagIndex = GetTagIndex(entity);
                if (tagIndex < 0 || tagIndex >= tagCount) continue;

                // Optimization: Use squared distance to avoid Sqrt until necessary
                float distSq = agentPos.DistanceSquaredTo(entity.Transform?.Position ?? agentPos);

                if (distSq <= nearDistSq)
                {
                    _proximityNear[tagIndex] = true;
                }

                // Count ALL entities for planning purposes, regardless of reservation status.
                // The planner should see the full world state - actions handle contention at runtime.
                // This prevents bizarre long plans when other agents have reserved resources.
                if (_availabilityCounts[tagIndex] < MAX_AVAILABLE_PER_TYPE)
                {
                    _availabilityCounts[tagIndex]++;
                }

                // Only Sqrt if we have a new candidate for nearest
                if (distSq < _nearestDistances[tagIndex] * _nearestDistances[tagIndex])
                {
                    _nearestDistances[tagIndex] = MathF.Sqrt(distSq);
                }
            }
        }
    }

    private static bool HasAnyTargetTag(Entity e)
    {
        foreach (var tag in _cachedTargetTags)
        {
            if (e.HasTag(tag)) return true;
        }
        return false;
    }

    private static int GetTagIndex(Entity e)
    {
        for (int i = 0; i < _cachedTargetTags.Length; i++)
        {
            if (e.HasTag(_cachedTargetTags[i])) return i;
        }
        return -1;
    }

    private void ApplyProximityFacts(State state)
    {
        for (int i = 0; i < _cachedTargetTags.Length; i++)
        {
            state.Set(_nearFactIds[i], _proximityNear[i]);
            state.Set(_worldCountFactIds[i], _availabilityCounts[i]);
            state.Set(_worldHasFactIds[i], _availabilityCounts[i] > 0);

            if (_nearestDistances[i] < float.MaxValue)
            {
                state.Set(_distanceFactIds[i], _nearestDistances[i]);
            }
        }
    }

    private void AddNPCFacts(State state, NPCData npcData)
    {
        for (int i = 0; i < _cachedTargetTags.Length; i++)
        {
            int agentCount = npcData.Resources.GetValueOrDefault(_cachedTargetTags[i], 0);

            state.Set(_agentCountFactIds[i], agentCount);
            state.Set(_agentHasFactIds[i], agentCount > 0);
        }

        state.Set(HungerFactId, npcData.Hunger);
        state.Set(IsHungryFactId, npcData.Hunger > 30f); // Explicit boolean for simple preconditions
        state.Set(SleepinessFactId, npcData.Sleepiness);
        state.Set(IsSleepyFactId, npcData.Sleepiness > 70f);
    }

    private bool ShouldRefreshProximity(float currentTime)
    {
        if (FrameTime.FrameIndex == _lastProximityQueryFrame && !_proximityRefreshPending)
        {
            return false;
        }

        bool intervalElapsed = (currentTime - _lastProximityUpdate) >= PROXIMITY_UPDATE_INTERVAL;

        if (!intervalElapsed && !_proximityRefreshPending)
        {
            if (Entity.TryGetComponent<TransformComponent2D>(out var transform))
            {
                float distanceMoved = transform.Position.DistanceTo(_lastProximityQueryPosition);
                if (distanceMoved < 128f)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
