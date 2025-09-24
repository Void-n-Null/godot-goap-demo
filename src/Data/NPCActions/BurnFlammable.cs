using Game.Data;
using Game.Data.NPCActions;
using Game.Utils;
using Game.Data.Components;
using System.Linq;

namespace Game.Data.NPCActions;

public class BurnFlammable : INPCAction
{
    private Entity _nearestFlammable;
    private NPCMotorComponent _npcMotor;
    public bool IsComplete { get; set; }
    public void Prepare(Entity entity)
    {
        //Lets look if we can find a flammable entity!
        TryFindNearestFlammable(entity, out _nearestFlammable);
        _npcMotor = entity.GetComponent<NPCMotorComponent>();
        _npcMotor.OnTargetReached += _nearestFlammable.GetComponent<FlammableComponent>().SetOnFire;
    }

    private static bool TryFindNearestFlammable(Entity entity, out Entity nearestFlammableNotOnFire)
    {
        nearestFlammableNotOnFire = GetEntities.WithComponent<FlammableComponent>().Where(e => !e.GetComponent<FlammableComponent>().IsBurning).OrderBy(e => e.Transform.Position.DistanceTo(entity.Transform.Position)).FirstOrDefault();
        return nearestFlammableNotOnFire != null;
    }

    public void OnUpdate(Entity entity, double delta)
    {
        if (_nearestFlammable == null){
            return;
        }
        if (_nearestFlammable.GetComponent<FlammableComponent>().IsBurning){
            IsComplete = true;
        }

        _npcMotor.Target = _nearestFlammable.Transform.Position;
    }


    public void OnStop(Entity entity)
    {
        _nearestFlammable = null;
    }
}