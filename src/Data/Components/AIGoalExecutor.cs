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
using TargetType = Game.Data.Components.TargetType;

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

    private static readonly TargetType[] _cachedTargetTypes = (TargetType[])Enum.GetValues(typeof(TargetType));
    private static readonly string[] _cachedTargetTypeStrings;
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
        _cachedTargetTypeStrings = new string[_cachedTargetTypes.Length];
        _nearFactIds = new int[_cachedTargetTypes.Length];
        _worldCountFactIds = new int[_cachedTargetTypes.Length];
        _worldHasFactIds = new int[_cachedTargetTypes.Length];
        _agentCountFactIds = new int[_cachedTargetTypes.Length];
        _agentHasFactIds = new int[_cachedTargetTypes.Length];
        _distanceFactIds = new int[_cachedTargetTypes.Length];
        HungerFactId = FactRegistry.GetId("Hunger");
        IsHungryFactId = FactRegistry.GetId("IsHungry");
        SleepinessFactId = FactRegistry.GetId("Sleepiness");
        IsSleepyFactId = FactRegistry.GetId("IsSleepy");

        for (int i = 0; i < _cachedTargetTypes.Length; i++)
        {
            string typeName = _cachedTargetTypes[i].ToString();
            _cachedTargetTypeStrings[i] = typeName;
            var type = _cachedTargetTypes[i];
            _nearFactIds[i] = FactRegistry.GetId(FactKeys.NearTarget(type));
            _worldCountFactIds[i] = FactRegistry.GetId(FactKeys.WorldCount(type));
            _worldHasFactIds[i] = FactRegistry.GetId(FactKeys.WorldHas(type));
            _agentCountFactIds[i] = FactRegistry.GetId(FactKeys.AgentCount(type));
            _agentHasFactIds[i] = FactRegistry.GetId(FactKeys.AgentHas(type));
            _distanceFactIds[i] = FactRegistry.GetId($"Distance_To_{type}");
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
                _currentPlan = _planningTask.Result;
                PlanningInProgress = false;
                _planningTask = null;

                if (_currentPlan != null && _currentPlan.Steps.Count > 0)
                {
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

            if (tickResult)
            {
                if (_currentPlan.Succeeded)
                {
                    _currentPlan = null;
                    _stateDirty = true;
                    _consecutiveFailures = 0;
                    _forceReplan = true; // Always replan immediately after success (e.g. for continuous goals like Idle)
                    OnPlanSucceeded?.Invoke();
                }
                else
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
            }
        }
    }

    public void OnStart()
    {
        _cachedState ??= new State();
        _proximityNear ??= new bool[_cachedTargetTypes.Length];
        _availabilityCounts ??= new int[_cachedTargetTypes.Length];
        _nearestDistances ??= new float[_cachedTargetTypes.Length];
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
            FactKeys.AgentCount(TargetType.Stick),
            FactKeys.WorldCount(TargetType.Stick),
            FactKeys.WorldHas(TargetType.Stick),
            FactKeys.WorldCount(TargetType.Tree),
            FactKeys.WorldHas(TargetType.Tree),
            FactKeys.WorldCount(TargetType.Food),
            FactKeys.WorldHas(TargetType.Food),
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
        // Fast check using new HasComponent logic
        if (!entity.HasComponent<TargetComponent>()) return;

        // Ignore far away events so one new campfire doesn't reset the entire population
        if (Entity.TryGetComponent<TransformComponent2D>(out var selfTransform) &&
            entity.TryGetComponent<TransformComponent2D>(out var eventTransform))
        {
            if (selfTransform.Position.DistanceSquaredTo(eventTransform.Position) > WORLD_EVENT_RADIUS_SQ)
            {
                return;
            }
        }

        // Only force replan if the event is relevant to our current goal
        if (!entity.TryGetComponent<TargetComponent>(out var targetComp)) return;

        var currentGoalState = _currentGoal?.GetGoalState(Entity);
        if (currentGoalState != null && !GoalReferencesTarget(currentGoalState, targetComp.Target))
        {
            return;
        }

        _stateDirty = true;
        _lastStateBuildTime = float.NegativeInfinity; // force immediate rebuild next tick
        _forceReplan = true;
        _proximityRefreshPending = true;

        if (_currentPlan != null && !_currentPlan.IsComplete)
        {
            if (currentGoalState != null)
            {
                LM.Info($"[{Entity.Name}] World event for {targetComp.Target} invalidated current plan, cancelling.");
                _currentPlan.Cancel(Entity);
            }
        }
    }

    private bool GoalReferencesTarget(State goalState, TargetType targetType)
    {
        if (_currentGoal is IUtilityGoalTargetInterest interest && interest.IsTargetTypeRelevant(targetType))
        {
            return true;
        }

        var nearKey = FactKeys.NearTarget(targetType);
        var worldHasKey = FactKeys.WorldHas(targetType);
        var worldCountKey = FactKeys.WorldCount(targetType);

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

        // Optimized Query: Use HasComponent (O(1) for sealed)
        var nearbyEntities = Universe.EntityManager.Instance?.SpatialPartition?.QueryCircle(
            agentPos,
            searchRadius,
            e => e.HasComponent<TargetComponent>(),
            MAX_PROXIMITY_RESULTS);

        if (nearbyEntities != null)
        {
            var reservationManager = Universe.ResourceReservationManager.Instance;
            int targetTypeCount = _cachedTargetTypes.Length;
            const float nearDistSq = 64f * 64f;

            // Use indexed access to avoid enumerator allocation? List<T>.Enumerator is struct, so it's fine.
            foreach (var entity in nearbyEntities)
            {
                if (!entity.TryGetComponent<TargetComponent>(out var tc)) continue;

                // Optimization: Map Enum directly to index
                int typeIndex = (int)tc.Target;
                if (typeIndex < 0 || typeIndex >= targetTypeCount) continue;

                // Optimization: Use squared distance to avoid Sqrt until necessary
                float distSq = agentPos.DistanceSquaredTo(entity.Transform?.Position ?? agentPos);

                if (distSq <= nearDistSq)
                {
                    _proximityNear[typeIndex] = true;
                }

                // HOT PATH OPTIMIZATION: Uses Entity.ReservedByAgentId (O(1))
                bool isAvailable = reservationManager.IsAvailableFor(entity, agent);
                if (isAvailable && _availabilityCounts[typeIndex] < MAX_AVAILABLE_PER_TYPE)
                {
                    _availabilityCounts[typeIndex]++;
                }

                // Only Sqrt if we have a new candidate for nearest
                // Use distSq for comparison
                if (distSq < _nearestDistances[typeIndex] * _nearestDistances[typeIndex])
                {
                    _nearestDistances[typeIndex] = MathF.Sqrt(distSq);
                }
            }
        }
    }

    private void ApplyProximityFacts(State state)
    {
        for (int i = 0; i < _cachedTargetTypes.Length; i++)
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
        for (int i = 0; i < _cachedTargetTypes.Length; i++)
        {
            int agentCount = npcData.Resources.GetValueOrDefault(_cachedTargetTypes[i], 0);

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
