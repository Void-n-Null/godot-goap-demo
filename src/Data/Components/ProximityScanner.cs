using System;
using System.Collections.Generic;
using Game.Data.GOAP;
using Game.Data.Components;
using Game.Universe;
using Game.Data;
using Game.Utils;
using Godot;

namespace Game.Data.Components;

#nullable enable

/// <summary>
/// Handles proximity queries and applies related facts to agent state.
/// </summary>
public sealed class ProximityScanner
{
    private readonly GoalFactRegistry _facts;
    private readonly Tag[] _targetTags;

    private bool[] _proximityNear = [];
    private int[] _availabilityCounts = [];
    private float[] _nearestDistancesSq = [];

    private ulong _lastProximityQueryFrame;
    private Vector2 _lastProximityQueryPosition;
    private bool _proximityRefreshPending;
    private float _lastProximityUpdate = -PROXIMITY_UPDATE_INTERVAL;

    private const float PROXIMITY_UPDATE_INTERVAL = 0.25f;
    private const float WORLD_EVENT_RADIUS = 1000f;
    private const float WORLD_EVENT_RADIUS_SQ = WORLD_EVENT_RADIUS * WORLD_EVENT_RADIUS;
    private const int MAX_PROXIMITY_RESULTS = 256;
    private const int MAX_AVAILABLE_PER_TYPE = 6;

    public ProximityScanner(GoalFactRegistry facts)
    {
        _facts = facts;
        _targetTags = facts.TargetTags;
        EnsureBuffers();
    }

    public bool HandleWorldEvent(Entity self, Entity evt, Func<Tag, bool> goalTagRelevant)
    {
        if (!HasAnyTargetTag(evt)) return false;

        if (self.TryGetComponent<TransformComponent2D>(out var selfTransform) &&
            evt.TryGetComponent<TransformComponent2D>(out var evtTransform))
        {
            if (selfTransform.Position.DistanceSquaredTo(evtTransform.Position) > WORLD_EVENT_RADIUS_SQ)
            {
                return false;
            }
        }

        var entityTag = GetFirstTargetTag(evt);
        if (!goalTagRelevant(entityTag)) return false;

        _proximityRefreshPending = true;
        _lastProximityUpdate = -PROXIMITY_UPDATE_INTERVAL;
        return true;
    }

    public void RebuildAndApply(State state, TransformComponent2D agentTransform, Entity agent, float currentTime)
    {
        EnsureBuffers();
        UpdateProximityAndAvailability(agentTransform, agent);
        ApplyProximityFacts(state);

        _lastProximityUpdate = currentTime;
        _lastProximityQueryFrame = FrameTime.FrameIndex;
        _lastProximityQueryPosition = agentTransform.Position;
        _proximityRefreshPending = false;
    }

    public bool ShouldRefresh(float currentTime, TransformComponent2D transform)
    {
        if (FrameTime.FrameIndex == _lastProximityQueryFrame && !_proximityRefreshPending)
        {
            return false;
        }

        bool intervalElapsed = (currentTime - _lastProximityUpdate) >= PROXIMITY_UPDATE_INTERVAL;

        if (!intervalElapsed && !_proximityRefreshPending)
        {
            float distanceMoved = transform.Position.DistanceTo(_lastProximityQueryPosition);
            if (distanceMoved < 128f)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateProximityAndAvailability(TransformComponent2D agentTransform, Entity agent)
    {
        var agentPos = agentTransform.Position;
        const float searchRadius = 5000f;

        Array.Fill(_proximityNear, false);
        Array.Clear(_availabilityCounts, 0, _availabilityCounts.Length);
        for (int i = 0; i < _nearestDistancesSq.Length; i++)
        {
            _nearestDistancesSq[i] = float.PositiveInfinity;
        }

        // Query without predicate to avoid double tag iteration.
        // GetTagIndex already returns -1 for non-target entities, so we filter inline.
        var nearbyEntities = Universe.EntityManager.Instance?.SpatialPartition?.QueryCircle(
            agentPos,
            searchRadius,
            predicate: null,
            MAX_PROXIMITY_RESULTS * 4);  // Higher limit since we filter after

        if (nearbyEntities != null)
        {
            int tagCount = _facts.TargetTags.Length;
            const float nearDistSq = 64f * 64f;
            int matchCount = 0;

            foreach (var entity in nearbyEntities)
            {
                // GetTagIndex returns -1 for non-target entities (one tag iteration instead of two)
                int tagIndex = GetTagIndex(entity);
                if ((uint)tagIndex >= (uint)tagCount) continue;

                // Early exit once we have enough matches
                if (++matchCount > MAX_PROXIMITY_RESULTS) break;

                float distSq = agentPos.DistanceSquaredTo(entity.Transform?.Position ?? agentPos);

                if (distSq <= nearDistSq)
                {
                    _proximityNear[tagIndex] = true;
                }

                if (_availabilityCounts[tagIndex] < MAX_AVAILABLE_PER_TYPE)
                {
                    _availabilityCounts[tagIndex]++;
                }

                if (distSq < _nearestDistancesSq[tagIndex])
                {
                    _nearestDistancesSq[tagIndex] = distSq;
                }
            }
        }
    }

    private void ApplyProximityFacts(State state)
    {
        for (int i = 0; i < _facts.TargetTags.Length; i++)
        {
            state.Set(_facts.NearFactIds[i], _proximityNear[i]);
            state.Set(_facts.WorldCountFactIds[i], _availabilityCounts[i]);
            state.Set(_facts.WorldHasFactIds[i], _availabilityCounts[i] > 0);

            if (_nearestDistancesSq[i] < float.PositiveInfinity)
            {
                state.Set(_facts.DistanceFactIds[i], MathF.Sqrt(_nearestDistancesSq[i]));
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool HasAnyTargetTag(Entity e)
    {
        // O(1) cached lookup - returns true if entity has any target tag
        return e.GetCachedTargetTagIndex() >= 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int GetTagIndex(Entity e)
    {
        // O(1) cached lookup - computed once per entity, cached until tags change
        return e.GetCachedTargetTagIndex();
    }

    private Tag GetFirstTargetTag(Entity e)
    {
        int idx = GetTagIndex(e);
        return idx >= 0 ? _facts.TargetTags[idx] : default;
    }

    private void EnsureBuffers()
    {
        int targetCount = _targetTags.Length;
        if (_proximityNear.Length != targetCount)
        {
            _proximityNear = new bool[targetCount];
        }
        if (_availabilityCounts.Length != targetCount)
        {
            _availabilityCounts = new int[targetCount];
        }
        if (_nearestDistancesSq.Length != targetCount)
        {
            _nearestDistancesSq = new float[targetCount];
        }
    }

}

