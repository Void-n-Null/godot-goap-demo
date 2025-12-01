using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Game.Data;
using Godot;
using System.Runtime.CompilerServices;
using Game.Utils;

namespace Game.Universe;

/// <summary>
/// Manages resource reservations to prevent multiple agents from targeting the same resource
/// OPTIMIZED: Uses ConcurrentDictionary for lock-free reads in hot paths
/// </summary>
public class ResourceReservationManager
{
    private static ResourceReservationManager _instance;
    public static ResourceReservationManager Instance => _instance ??= new ResourceReservationManager();

    private ResourceReservationManager()
    {
        // Subscribe to entity despawn events for automatic cleanup
        WorldEventBus.Instance.EntityDespawned += OnEntityDespawned;
    }

    // OPTIMIZED: ConcurrentDictionary allows lock-free reads (critical for IsAvailableFor hot path)
    // Maps resource entity ID to the agent ID that has reserved it
    private ConcurrentDictionary<Guid, Guid> _reservations = new();

    // Maps agent ID to all resources they've reserved (for cleanup)
    // Still needs lock since HashSet isn't thread-safe
    private Dictionary<Guid, HashSet<Guid>> _agentReservations = new();

    // Lock only needed for _agentReservations now
    private readonly object _lock = new object();

    /// <summary>
    /// Try to reserve a resource for an agent
    /// </summary>
    public bool TryReserve(Entity resource, Entity agent)
    {
        if (resource == null || agent == null)
            return false;

        var resourceId = resource.Id;
        var agentId = agent.Id;

        // OPTIMIZED: Use ConcurrentDictionary.TryAdd for atomic reservation attempt
        // This is lock-free and thread-safe
        bool wasAdded = _reservations.TryAdd(resourceId, agentId);

        if (!wasAdded)
        {
            // Resource already reserved - check if it's by us
            if (_reservations.TryGetValue(resourceId, out var existingAgentId) && existingAgentId == agentId)
            {
                return true; // Already reserved by this agent
            }
            return false; // Reserved by someone else
        }

        // Successfully reserved in dict, update Entity cache
        resource.ReservedByAgentId = agentId;

        // Successfully reserved - update agent tracking (this still needs lock since HashSet isn't thread-safe)
        lock (_lock)
        {
            if (!_agentReservations.ContainsKey(agentId))
            {
                _agentReservations[agentId] = new HashSet<Guid>();
            }
            _agentReservations[agentId].Add(resourceId);
        }

        LM.Debug($"[Reservation] {agent.Name} reserved {resource.Name} ({resourceId})");
        return true;
    }

    /// <summary>
    /// Release a specific resource reservation
    /// </summary>
    public void Release(Entity resource, Entity agent)
    {
        if (resource == null || agent == null)
            return;

        var resourceId = resource.Id;
        var agentId = agent.Id;

        // OPTIMIZED: Use ConcurrentDictionary.TryRemove for atomic removal
        // Only remove if it's actually reserved by this agent
        if (_reservations.TryGetValue(resourceId, out var reservedBy) && reservedBy == agentId)
        {
            _reservations.TryRemove(resourceId, out _);
            resource.ReservedByAgentId = Guid.Empty; // Clear cache

            // Update agent tracking (still needs lock for HashSet)
            lock (_lock)
            {
                _agentReservations[agentId]?.Remove(resourceId);
            }

            LM.Debug($"[Reservation] {agent.Name} released {resource.Name} ({resourceId})");
        }
    }

    /// <summary>
    /// Release all reservations held by an agent
    /// </summary>
    public void ReleaseAll(Entity agent)
    {
        if (agent == null)
            return;

        var agentId = agent.Id;

        lock (_lock)
        {
            if (_agentReservations.TryGetValue(agentId, out var reservedResources))
            {
                // Remove from ConcurrentDictionary and clear entity cache fields
                foreach (var resourceId in reservedResources)
                {
                    if (_reservations.TryRemove(resourceId, out _))
                    {
                        // Look up entity and clear cached field
                        var ent = EntityManager.Instance?.GetEntityById(resourceId);
                        if (ent != null)
                        {
                            ent.ReservedByAgentId = Guid.Empty;
                        }
                    }
                }
                reservedResources.Clear();
                _agentReservations.Remove(agentId);
                LM.Info($"[Reservation] {agent.Name} released all reservations");
            }
        }
    }

    /// <summary>
    /// Handle entity despawn events to automatically clean up reservations
    /// </summary>
    private void OnEntityDespawned(Entity entity)
    {
        if (entity == null)
            return;

        var entityId = entity.Id;

        // If this entity was a resource, release its reservation
        if (_reservations.TryRemove(entityId, out var agentId))
        {
            lock (_lock)
            {
                _agentReservations[agentId]?.Remove(entityId);
            }
            LM.Debug($"[Reservation] Auto-released despawned resource {entity.Name}");
        }

        // If this entity was an agent, release all its reservations
        lock (_lock)
        {
            if (_agentReservations.TryGetValue(entityId, out var reservedResources))
            {
                foreach (var resourceId in reservedResources)
                {
                    if (_reservations.TryRemove(resourceId, out _))
                    {
                        var resource = EntityManager.Instance?.GetEntityById(resourceId);
                        if (resource != null)
                        {
                            resource.ReservedByAgentId = Guid.Empty;
                        }
                    }
                }
                _agentReservations.Remove(entityId);
                LM.Debug($"[Reservation] Auto-released all reservations for despawned agent {entity.Name}");
            }
        }
    }

    /// <summary>
    /// Check if a resource is reserved
    /// OPTIMIZED: Lock-free read using ConcurrentDictionary
    /// </summary>
    public bool IsReserved(Entity resource)
    {
        if (resource == null)
            return false;

        // Fast path
        if (resource.ReservedByAgentId != Guid.Empty) return true;

        // Fallback to dict? (Should remain synced)
        return _reservations.ContainsKey(resource.Id);
    }

    /// <summary>
    /// Check if a resource is reserved by a specific agent
    /// OPTIMIZED: Lock-free read using ConcurrentDictionary
    /// </summary>
    public bool IsReservedBy(Entity resource, Entity agent)
    {
        if (resource == null || agent == null)
            return false;

        // Fast path
        if (resource.ReservedByAgentId == agent.Id) return true;

        return _reservations.TryGetValue(resource.Id, out var reservedBy) && reservedBy == agent.Id;
    }

    /// <summary>
    /// Check if a resource is available (not reserved or reserved by this agent)
    /// OPTIMIZED: Lock-free read using cached Entity field (HOT PATH)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAvailableFor(Entity resource, Entity agent)
    {
        if (resource == null)
            return false;

        // HOT PATH OPTIMIZATION:
        // Check cached field directly.
        // If ReservedByAgentId is Empty -> Available.
        // If ReservedByAgentId matches agent.Id -> Available (we own it).
        // If ReservedByAgentId matches someone else -> Unavailable.
        
        var reservedBy = resource.ReservedByAgentId;
        if (reservedBy == Guid.Empty) return true;
        
        if (agent != null && reservedBy == agent.Id) return true;

        return false;
    }

    /// <summary>
    /// BATCH API: Filter a list of entities to only those available for the agent
    /// More efficient than calling IsAvailableFor in a loop - processes all in one pass
    /// </summary>
    public void FilterAvailable(IEnumerable<Entity> resources, Entity agent, List<Entity> results)
    {
        if (resources == null || results == null)
            return;

        var agentId = agent?.Id ?? Guid.Empty;
        foreach (var resource in resources)
        {
            if (resource == null) continue;

            var reservedBy = resource.ReservedByAgentId;
            if (reservedBy == Guid.Empty || reservedBy == agentId)
            {
                results.Add(resource);
            }
        }
    }
}
