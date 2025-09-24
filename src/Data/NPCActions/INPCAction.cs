using Game.Data;

namespace Game.Data.NPCActions;

public interface INPCAction
{
    bool IsComplete { get; set; }
    virtual void Prepare(Entity entity) { }
    virtual void OnStart(Entity entity) { }
    virtual void OnStop(Entity entity) { }
    virtual void OnUpdate(Entity entity, double delta) { }
}