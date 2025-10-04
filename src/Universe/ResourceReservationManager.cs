using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Game.Data;
using Godot;

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
                    _reservations.TryRemove(resourceId, out _);
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

        // OPTIMIZED: No lock needed - ConcurrentDictionary.ContainsKey is thread-safe
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

        // OPTIMIZED: No lock needed - ConcurrentDictionary.TryGetValue is thread-safe
        return _reservations.TryGetValue(resource.Id, out var reservedBy) && reservedBy == agent.Id;
    }

    /// <summary>
    /// Check if a resource is available (not reserved or reserved by this agent)
    /// OPTIMIZED: Lock-free read using ConcurrentDictionary (HOT PATH - called 100+ times per frame per agent)
    /// </summary>
    public bool IsAvailableFor(Entity resource, Entity agent)
    {
        if (resource == null)
            return false;

        var resourceId = resource.Id;

        // OPTIMIZED: No lock needed - ConcurrentDictionary.TryGetValue is thread-safe
        // Not reserved = available
        if (!_reservations.TryGetValue(resourceId, out var reservedBy))
            return true;

        // Reserved by this agent = available
        if (agent != null && reservedBy == agent.Id)
            return true;

        // Reserved by someone else = not available
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

        // No lock needed - we're just doing reads with ConcurrentDictionary
        var agentId = agent?.Id;
        foreach (var resource in resources)
        {
            if (resource == null)
                continue;

            var resourceId = resource.Id;

            // Not reserved = available
            if (!_reservations.TryGetValue(resourceId, out var reservedBy))
            {
                results.Add(resource);
                continue;
            }

            // Reserved by this agent = available
            if (agentId.HasValue && reservedBy == agentId.Value)
            {
                results.Add(resource);
            }
            // Reserved by someone else = skip
        }
    }
}
