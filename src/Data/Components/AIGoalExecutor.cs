using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data.GOAP;
using Game.Universe;
using Game.Data.UtilityAI;
using Game.Utils;

#nullable enable
namespace Game.Data.Components;

/// <summary>
/// Executes plans to achieve goals. Fires events to notify selector of status changes.
/// </summary>
public class AIGoalExecutor : IActiveComponent
{
    public Entity Entity { get; set; } = null!;

    public AIGoalEvents Events { get; } = new();

    private readonly PlanRunner _planRunner = new();
    private readonly PlanCoordinator _planCoordinator = new();
    private PlanningJob? _planningJob;
    public bool PlanningInProgress { get; private set; }
    private IUtilityGoal? _currentGoal;

    private float _lastPlanningTime;
    private const float MIN_PLANNING_INTERVAL = 0.1f;
    private bool _forceReplan;

    private int _consecutiveFailures;
    private const int MAX_IMMEDIATE_RETRIES = 2;

    private static readonly GoalFactRegistry Facts = new();

    private bool _worldEventsSubscribed;
    private IDisposable? _spawnSub;
    private IDisposable? _despawnSub;

    private readonly ProximityScanner _proximityScanner = new ProximityScanner(Facts);

    private readonly AgentStateCache _stateCache;

    public Plan? ActivePlan => _planRunner.CurrentPlan;
    public int ActivePlanStepIndex => _planRunner.CurrentPlan?.CurrentStepIndex ?? -1;

    public AIGoalExecutor()
    {
        _stateCache = new AgentStateCache(
            Facts,
            _proximityScanner);
    }


    public void SetGoal(IUtilityGoal goal)
    {
        if (_currentGoal != goal)
        {
            var oldGoal = _currentGoal?.Name ?? "None";
            var newGoal = goal?.Name ?? "None";
            var hadPlan = _planRunner.HasActivePlan;
            LM.Debug($"[{Entity?.Name}] SetGoal: {oldGoal} -> {newGoal} (had active plan: {hadPlan})");
            
            _currentGoal = goal;
            _planRunner.ClearPlan();
            _stateCache.MarkDirty();
            _forceReplan = true; // New goal requires new plan immediately

            UpdateWorldEventSubscriptionsForCurrentGoal();
        }
    }

    public void CancelCurrentPlan()
    {
        if (_planRunner.HasActivePlan)
        {
            _planRunner.CancelPlan(Entity);
            _stateCache.MarkDirty();
            _forceReplan = true;
        }
    }

    public void Update(double delta)
    {
        float currentTime = GameManager.Instance.CachedTimeMsec / 1000.0f;

        // Watchdog: If we have no plan and aren't planning for too long, force a kick
        if (!_planRunner.HasActivePlan && !PlanningInProgress && _currentGoal != null)
        {
            if (currentTime - _lastPlanningTime > 2.0f) // 2 seconds stuck?
            {
                LM.Warning($"[{Entity.Name}] Watchdog: Stuck without plan for 2s. Forcing replan.");
                _forceReplan = true;
            }
        }

        var currentState = _stateCache.GetOrBuildState(currentTime, Entity);

        // Handle async planning completion via coordinator
        if (PlanningInProgress && _planningJob?.Task != null)
        {
            var job = _planningJob;
            var completion = _planCoordinator.ProcessCompletion(job, _currentGoal, Entity, _planRunner);

            if (completion != PlanCompletionResult.StillRunning)
            {
                PlanningInProgress = false;
                _planningJob = null;
            }

            switch (completion)
            {
                case PlanCompletionResult.Installed:
                    _consecutiveFailures = 0;
                    break;
                case PlanCompletionResult.NoPath:
                    var failedGoal = _currentGoal;
                    _currentGoal = null;
                    _planRunner.ClearPlan();
                    _consecutiveFailures = 0;
                    _stateCache.MarkDirty();
                    Events.NotifyCannotPlan(failedGoal);
                    return;
                case PlanCompletionResult.Faulted:
                    var faultedGoal = _currentGoal;
                    _currentGoal = null;
                    _stateCache.MarkDirty();
                    Events.NotifyCannotPlan(faultedGoal);
                    return;
                case PlanCompletionResult.StaleGoal:
                case PlanCompletionResult.ActivePlanExists:
                case PlanCompletionResult.StillRunning:
                default:
                    break;
            }
        }

        if (_currentGoal != null && _currentGoal.IsSatisfied(Entity))
        {
            LM.Info($"[{Entity.Name}] Goal '{_currentGoal.Name}' satisfied, clearing plan and goal");
            _planRunner.ClearPlan();
            _currentGoal = null;
            Events.NotifyGoalSatisfied();
            return;
        }

        if (_currentGoal == null)
        {
            Events.NotifyNeedNewGoal();
            return;
        }

        if (!_planRunner.HasActivePlan && _currentGoal != null && !PlanningInProgress)
        {
            float planningTime = GameManager.Instance.CachedTimeMsec / 1000.0f;
            bool shouldPlan = _forceReplan;

            if (!shouldPlan)
            {
                if (planningTime - _lastPlanningTime < MIN_PLANNING_INTERVAL)
                {
                    return;
                }
                shouldPlan = _stateCache.HasWorldStateChangedSignificantly(currentState);
            }

            if (shouldPlan)
            {
                PlanningInProgress = true;

                // Clone current state to last planning state
                _stateCache.CaptureLastPlanningState(currentState);
                _lastPlanningTime = planningTime;
                _forceReplan = false;

                var goalState = _currentGoal.GetGoalState(Entity);
                LM.Debug($"[{Entity.Name}] Starting planning task for '{_currentGoal?.Name}'");
                // Need to pass a clone because planner might mutate it during search (or A* node expansion)
                _planningJob = _planCoordinator.BeginPlanning(currentState.Clone(), goalState, _currentGoal!);
            }
        }

        if (_planRunner.HasActivePlan)
        {
            var tickResult = _planRunner.Tick(Entity, _currentGoal, delta);

            if (tickResult == PlanRunStatus.Succeeded)
            {
                _stateCache.MarkDirty();
                _consecutiveFailures = 0;
                _forceReplan = true; // Always replan immediately after success (e.g. for continuous goals like Idle)
                Events.NotifyPlanSucceeded();
            }
            else if (tickResult == PlanRunStatus.Failed)
            {
                LM.Warning($"[{Entity.Name}] Plan execution failed for '{_currentGoal?.Name}'");
                var failedGoal = _currentGoal;
                _stateCache.MarkDirty();
                _consecutiveFailures++;

                _forceReplan = true;
                if (_consecutiveFailures > MAX_IMMEDIATE_RETRIES)
                {
                    LM.Warning($"[{Entity.Name}] Too many consecutive failures ({_consecutiveFailures}), waiting before retry");
                }

                GlobalWorldStateManager.Instance.ForceRefresh();
                Events.NotifyPlanExecutionFailed(failedGoal);
            }
            // If tickResult is Running or NoPlan, continue executing next frame
        }
    }

    public void OnStart()
    {
        SubscribeToWorldEvents();
    }


    public void OnDetached()
    {
        if (_worldEventsSubscribed)
        {
            WorldEventBus.Instance.EntitySpawned -= OnWorldTargetEntityChanged;
            WorldEventBus.Instance.EntityDespawned -= OnWorldTargetEntityChanged;
        }
        _spawnSub?.Dispose();
        _despawnSub?.Dispose();
        _worldEventsSubscribed = false;
    }

    private void SubscribeToWorldEvents()
    {
        if (_worldEventsSubscribed) return;
        WorldEventBus.Instance.EntitySpawned += OnWorldTargetEntityChanged;
        WorldEventBus.Instance.EntityDespawned += OnWorldTargetEntityChanged;
        _worldEventsSubscribed = true;
        UpdateWorldEventSubscriptionsForCurrentGoal();
    }

    private void UpdateWorldEventSubscriptionsForCurrentGoal()
    {
        _spawnSub?.Dispose();
        _despawnSub?.Dispose();

        var tags = BuildRelevantTagsForCurrentGoal();
        if (tags.Count == 0) return;

        _spawnSub = WorldEventBus.Instance.SubscribeSpawnedForTags(tags, OnWorldTargetEntityChanged);
        var despawnTags = BuildRelevantDespawnTagsForCurrentGoal();
        if (despawnTags.Count > 0)
        {
            _despawnSub = WorldEventBus.Instance.SubscribeDespawnedForTags(despawnTags, OnWorldTargetEntityChanged);
        }
    }

    private List<Tag> BuildRelevantTagsForCurrentGoal()
    {
        var set = new HashSet<Tag>();
        if (_currentGoal is IUtilityGoalWorldEventInterest events)
        {
            foreach (var t in events.SpawnEventTags ?? Enumerable.Empty<Tag>())
            {
                set.Add(t);
            }
        }

        if (set.Count == 0)
        {
            if (_currentGoal is IUtilityGoalTagInterest interest)
            {
                for (int i = 0; i < Facts.TargetTags.Length; i++)
                {
                    var tag = Facts.TargetTags[i];
                    if (interest.IsTargetTagRelevant(tag))
                    {
                        set.Add(tag);
                    }
                }
            }
            else
            {
                // Default to all target tags if goal doesn't specify interest.
                for (int i = 0; i < Facts.TargetTags.Length; i++)
                {
                    set.Add(Facts.TargetTags[i]);
                }
            }
        }

        return set.ToList();
    }

    private List<Tag> BuildRelevantDespawnTagsForCurrentGoal()
    {
        var set = new HashSet<Tag>();
        if (_currentGoal is IUtilityGoalWorldEventInterest events)
        {
            foreach (var t in events.DespawnEventTags ?? Enumerable.Empty<Tag>())
            {
                set.Add(t);
            }
        }
        return set.ToList();
    }

    private void OnWorldTargetEntityChanged(Entity entity)
    {
        if (entity == null) return;

        var goalState = _currentGoal?.GetGoalState(Entity);
        bool goalRelevance(Tag tag) => goalState == null || GoalReferencesTag(goalState, tag);

        if (_proximityScanner.HandleWorldEvent(Entity, entity, goalRelevance))
        {
            _stateCache.MarkDirty();
            _stateCache.ForceImmediateRebuild(); // force immediate rebuild next tick

            if (!_planRunner.HasActivePlan)
            {
                _forceReplan = true;
            }
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

}
