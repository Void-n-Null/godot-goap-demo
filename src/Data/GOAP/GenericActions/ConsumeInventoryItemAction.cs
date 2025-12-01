using Game.Utils;
using Godot;

namespace Game.Data.GOAP.GenericActions;

public class ConsumeInventoryItemAction : IAction
{
    private readonly Tag _itemTag;
    private readonly float _duration;
    private float _timer;
    private bool _complete;
    private bool _failed;

    public string Name => $"Consume {_itemTag}";

    public ConsumeInventoryItemAction(Tag itemTag, float duration = 2.0f)
    {
        _itemTag = itemTag;
        _duration = duration;
    }

    public void Enter(Entity agent)
    {
        _timer = 0f;
        _complete = false;
        _failed = false;
        
        if (agent.TryGetComponent<NPCData>(out var data))
        {
            if (!data.Resources.ContainsKey(_itemTag) || data.Resources[_itemTag] <= 0)
            {
                Fail($"ConsumeInventoryItemAction: Agent does not have {_itemTag}!");
            }
        }
    }

    public ActionStatus Update(Entity agent, float delta)
    {
        if (_failed) return ActionStatus.Failed;
        if (_complete) return ActionStatus.Succeeded;

        _timer += delta;
        if (_timer >= _duration)
        {
            if (agent.TryGetComponent<NPCData>(out var data))
            {
                if (data.Resources.TryGetValue(_itemTag, out int count) && count > 0)
                {
                    data.Resources[_itemTag] = count - 1;
                    
                    // Steak is more nutritious, but raw beef still works
                    int restored = _itemTag == Tags.Steak ? 40 : 25;
                    data.Hunger = Mathf.Max(0, data.Hunger - restored);
                    
                    LM.Info($"Consumed {_itemTag} from inventory. Hunger: {data.Hunger}");
                    _complete = true;
                    return ActionStatus.Succeeded;
                }
                else
                {
                     Fail("Item lost during consumption action");
                     return ActionStatus.Failed;
                }
            }
        }
        
        return ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason)
    {
        // Cleanup if needed
    }

    public void Fail(string reason)
    {
        LM.Error(reason);
        _failed = true;
    }
}
