using System.Collections.Generic;
using Game.Data.Components;
using Game.Data.Blueprints.Objects;
using Game.Universe;
using Game.Utils;
using Godot;

namespace Game.Data.GOAP.GenericActions;

public class DepositItemAction : IAction
{
    private bool _complete;
    private bool _failed;
    private Tag _inputTag;
    private Tag _outputTag;
    
    public string Name => $"Deposit {_inputTag}";

    public DepositItemAction(Tag input, Tag output)
    {
        _inputTag = input;
        _outputTag = output;
    }

    public void Enter(Entity agent)
    {
        _complete = false;
        _failed = false;
        
        // Find nearest campfire
        int tested;
        var campfire = GetEntities.NearestByPredicateWithCounts(
            agent.Transform.Position, 
            150f, 
            e => e.TryGetComponent<CookingStationComponent>(out var s) && !s.IsCooking && !s.HasCookedItem,
            out tested
        );

        if (campfire != null && campfire.TryGetComponent<CookingStationComponent>(out var station))
        {
            if (agent.TryGetComponent<NPCData>(out var data))
            {
                if (data.Resources.TryGetValue(_inputTag, out int count) && count > 0)
                {
                    // Remove item
                    data.Resources[_inputTag] = count - 1;
                    
                    // Start cooking - map tags to blueprints
                    EntityBlueprint inputBP = _inputTag == Tags.RawBeef ? Food.RawBeef : null;
                    EntityBlueprint outputBP = _outputTag == Tags.Steak ? Food.Steak : null;
                    
                    if (inputBP != null && outputBP != null)
                    {
                        // In a full system, we'd extract this from the Blueprint metadata.
                        // For this demo, we map known tags to their cook times.
                        float cookTime = _inputTag == Tags.RawBeef ? 5.0f : 5.0f;

                        station.StartCooking(inputBP, outputBP, cookTime);
                        LM.Info($"Deposited {_inputTag} into {campfire.Name}. Cooking started (Time: {cookTime}s)!");
                        _complete = true;
                    }
                    else
                    {
                         Fail($"DepositItemAction: Could not map tags to blueprints: {_inputTag}->{_outputTag}");
                    }
                }
                else
                {
                     Fail($"DepositItemAction: Agent has no {_inputTag}!");
                }
            }
        }
        else
        {
            Fail("DepositItemAction: No empty cooking station found!");
        }
    }

    public ActionStatus Update(Entity agent, float delta)
    {
        if (_failed) return ActionStatus.Failed;
        return _complete ? ActionStatus.Succeeded : ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason) { }
    
    public void Fail(string reason)
    {
        LM.Error(reason);
        _failed = true;
    }
}
