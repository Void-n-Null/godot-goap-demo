using System;
using System.Collections.Generic;
using Game.Data;
using Godot;

namespace Game.Universe;

/// <summary>
/// Manages resource reservations to prevent multiple agents from targeting the same resource
/// </summary>
public class ResourceReservationManager
{
    private static ResourceReservationManager _instance;
    public static ResourceReservationManager Instance => _instance ??= new ResourceReservationManager();

    // Maps resource entity ID to the agent ID that has reserved it
    private Dictionary<Guid, Guid> _reservations = new();
    
    // Maps agent ID to all resources they've reserved (for cleanup)
    private Dictionary<Guid, HashSet<Guid>> _agentReservations = new();
    
    // ✅ FIXED: Thread safety for parallel GOAP planning
    private readonly object _lock = new object();

    /// <summary>
    /// Try to reserve a resource for an agent
    /// </summary>
    public bool TryReserve(Entity resource, Entity agent)
    {
        if (resource == null || agent == null)
            return false;

        lock (_lock)
        {
            var resourceId = resource.Id;
            var agentId = agent.Id;

            // Already reserved by someone else
            if (_reservations.ContainsKey(resourceId) && _reservations[resourceId] != agentId)
            {
                return false;
            }

            // Already reserved by this agent
            if (_reservations.ContainsKey(resourceId) && _reservations[resourceId] == agentId)
            {
                return true;
            }

            // Reserve it
            _reservations[resourceId] = agentId;
            
            if (!_agentReservations.ContainsKey(agentId))
            {
                _agentReservations[agentId] = new HashSet<Guid>();
            }
            _agentReservations[agentId].Add(resourceId);

            GD.Print($"[Reservation] {agent.Name} reserved {resource.Name} ({resourceId})");
            return true;
        }
    }

    /// <summary>
    /// Release a specific resource reservation
    /// </summary>
    public void Release(Entity resource, Entity agent)
    {
        if (resource == null || agent == null)
            return;

        lock (_lock)
        {
            var resourceId = resource.Id;
            var agentId = agent.Id;

            if (_reservations.TryGetValue(resourceId, out var reservedBy) && reservedBy == agentId)
            {
                _reservations.Remove(resourceId);
                _agentReservations[agentId]?.Remove(resourceId);
                GD.Print($"[Reservation] {agent.Name} released {resource.Name} ({resourceId})");
            }
        }
    }

    /// <summary>
    /// Release all reservations held by an agent
    /// </summary>
    public void ReleaseAll(Entity agent)
    {
        if (agent == null)
            return;

        lock (_lock)
        {
            var agentId = agent.Id;

            if (_agentReservations.TryGetValue(agentId, out var reservedResources))
            {
                foreach (var resourceId in reservedResources)
                {
                    _reservations.Remove(resourceId);
                }
                reservedResources.Clear();
                GD.Print($"[Reservation] {agent.Name} released all reservations");
            }
        }
    }

    /// <summary>
    /// Check if a resource is reserved
    /// </summary>
    public bool IsReserved(Entity resource)
    {
        if (resource == null)
            return false;
            
        lock (_lock)
        {
            return _reservations.ContainsKey(resource.Id);
        }
    }

    /// <summary>
    /// Check if a resource is reserved by a specific agent
    /// </summary>
    public bool IsReservedBy(Entity resource, Entity agent)
    {
        if (resource == null || agent == null)
            return false;

        lock (_lock)
        {
            return _reservations.TryGetValue(resource.Id, out var reservedBy) && reservedBy == agent.Id;
        }
    }

    /// <summary>
    /// Check if a resource is available (not reserved or reserved by this agent)
    /// </summary>
    public bool IsAvailableFor(Entity resource, Entity agent)
    {
        if (resource == null)
            return false;

        lock (_lock)
        {
            var resourceId = resource.Id;
            
            // Not reserved = available
            if (!_reservations.ContainsKey(resourceId))
                return true;

            // Reserved by this agent = available
            if (agent != null && _reservations[resourceId] == agent.Id)
                return true;

            // Reserved by someone else = not available
            return false;
        }
    }
}
