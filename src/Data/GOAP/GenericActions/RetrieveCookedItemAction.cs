using System.Collections.Generic;
using Game.Data.Components;
using Game.Universe;
using Game.Utils;
using Godot;

namespace Game.Data.GOAP.GenericActions;

public class RetrieveCookedItemAction : IAction
{
    private bool _complete;
    private Entity _targetCampfire;
    private CookingStationComponent _station;
    private bool _failed;
    
    public string Name => "Retrieve Cooked Item";

    public void Enter(Entity agent)
    {
        _complete = false;
        _failed = false;
        
        // Find nearest campfire with a cooking station
        int tested;
        _targetCampfire = GetEntities.NearestByPredicateWithCounts(
            agent.Transform.Position, 
            150f, // Must be close
            e => e.TryGetComponent<CookingStationComponent>(out var s) && (s.IsCooking || s.HasCookedItem),
            out tested
        );

        if (_targetCampfire == null)
        {
            // Fallback: Find *any* campfire nearby
             _targetCampfire = GetEntities.NearestByPredicateWithCounts(
                agent.Transform.Position, 
                150f,
                e => e.TryGetComponent<CookingStationComponent>(out _),
                out tested
            );
        }

        if (_targetCampfire == null)
        {
            Fail("RetrieveCookedItemAction: No campfire found nearby!");
            return;
        }

        _station = _targetCampfire.GetComponent<CookingStationComponent>();
        LM.Info($"Waiting for cooking to complete at {_targetCampfire.Name}...");
    }

    public ActionStatus Update(Entity agent, float delta)
    {
        if (_failed) return ActionStatus.Failed;
        if (_complete) return ActionStatus.Succeeded;
        
        if (_station == null || _station.Entity == null || !_station.Entity.IsActive) 
        {
            Fail("Campfire invalid or destroyed.");
            return ActionStatus.Failed;
        }

        if (_station.HasCookedItem)
        {
            // Retrieve!
            var cookedBlueprint = _station.RetrieveItem();
            if (cookedBlueprint != null)
            {
                if (agent.TryGetComponent<NPCData>(out var data))
                {
                    var type = TargetType.Steak; // Hardcoded for now
                    data.Resources[type] = (data.Resources.TryGetValue(type, out int c) ? c : 0) + 1;
                    LM.Info($"Retrieved {cookedBlueprint.Name} (Steak) from campfire!");
                }
                
                _complete = true;
                return ActionStatus.Succeeded;
            }
        }
        
        // Continue waiting
        return ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason)
    {
        _targetCampfire = null;
        _station = null;
    }

    public void Fail(string reason)
    {
        LM.Error(reason);
        _failed = true;
    }
}
