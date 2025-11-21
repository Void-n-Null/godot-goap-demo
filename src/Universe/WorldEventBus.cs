using System;
using Game.Data;

namespace Game.Universe;

/// <summary>
/// Lightweight global event bus that broadcasts high-level world events
/// (entity spawn/despawn/etc) so interested systems can react immediately.
/// </summary>
public sealed class WorldEventBus
{
    private static readonly WorldEventBus _instance = new();
    public static WorldEventBus Instance => _instance;

    private WorldEventBus() { }

    public event Action<Entity> EntitySpawned;
    public event Action<Entity> EntityDespawned;

    public void PublishEntitySpawned(Entity entity)
    {
        EntitySpawned?.Invoke(entity);
    }

    public void PublishEntityDespawned(Entity entity)
    {
        EntityDespawned?.Invoke(entity);
    }
}

