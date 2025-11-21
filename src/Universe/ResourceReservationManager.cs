using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Game.Data;
using Godot;
using System.Runtime.CompilerServices;

namespace Game.Universe;

/// <summary>
/// Manages resource reservations to prevent multiple agents from targeting the same resource
/// OPTIMIZED: Uses ConcurrentDictionary for lock-free reads in hot paths
/// </summary>
public class ResourceReservationManager
{
    private static ResourceReservationManager _instance;
    public static ResourceReservationManager Instance => _instance ??= new ResourceReservationManager();

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

        GD.Print($"[Reservation] {agent.Name} reserved {resource.Name} ({resourceId})");
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

            GD.Print($"[Reservation] {agent.Name} released {resource.Name} ({resourceId})");
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
                // OPTIMIZED: Remove from ConcurrentDictionary without holding lock for each removal
                foreach (var resourceId in reservedResources)
                {
                    if (_reservations.TryRemove(resourceId, out _))
                    {
                        // We can't easily clear ReservedByAgentId on the entity instance here
                        // because we only have the Guid.
                        // However, this method is typically called on death or cleanup.
                        // The dictionary is the ground truth. 
                        // If IsAvailableFor checks the Entity field, it might see a stale reservation 
                        // if we don't clear it.
                        
                        // BUT, getting the Entity by ID is slow.
                        // If the entity is still alive, this is a problem.
                        // If the entity is dead, it doesn't matter.
                        
                        // Ideally we should look up the entity.
                        // For now, we accept the dictionary handles the logic correctness, 
                        // but the field is for speed.
                        // To be safe: IsAvailableFor should strictly trust the field only if it matches?
                        // No, IsAvailableFor is the hot path.
                        
                        // If we can't clear the field, we have a stale "Reserved" state on the resource entity.
                        // That resource will appear reserved by 'agent' (who released it).
                        // If 'agent' is dead, it's permanently reserved? That's BAD.
                        
                        // We MUST clear the field.
                        var ent = EntityManager.Instance?.GetEntityById(resourceId);
                        if (ent != null)
                        {
                            ent.ReservedByAgentId = Guid.Empty;
                        }
                    }
                }
                reservedResources.Clear();
                GD.Print($"[Reservation] {agent.Name} released all reservations");
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
