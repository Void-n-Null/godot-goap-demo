using System;
using System.Collections.Generic;
using Game.Data;

namespace Game.Universe;

/// <summary>
/// Lightweight global event bus that broadcasts high-level world events
/// (entity spawn/despawn/etc) so interested systems can react immediately.
/// Supports both broad and tag-filtered subscriptions.
/// </summary>
public sealed class WorldEventBus
{
    private static readonly WorldEventBus _instance = new();
    public static WorldEventBus Instance => _instance;

    private WorldEventBus() { }

    public event Action<Entity> EntitySpawned;
    public event Action<Entity> EntityDespawned;

    private readonly List<TagSubscription> _spawnTagSubscriptions = new();
    private readonly List<TagSubscription> _despawnTagSubscriptions = new();

    public IDisposable SubscribeSpawnedForTags(IEnumerable<Tag> tags, Action<Entity> handler)
        => AddTagSubscription(_spawnTagSubscriptions, tags, handler);

    public IDisposable SubscribeDespawnedForTags(IEnumerable<Tag> tags, Action<Entity> handler)
        => AddTagSubscription(_despawnTagSubscriptions, tags, handler);

    public void PublishEntitySpawned(Entity entity)
    {
        EntitySpawned?.Invoke(entity);
        DispatchTagSubscriptions(_spawnTagSubscriptions, entity);
    }

    public void PublishEntityDespawned(Entity entity)
    {
        // EntityDespawned?.Invoke(entity);
        // DispatchTagSubscriptions(_despawnTagSubscriptions, entity);
    }

    private IDisposable AddTagSubscription(List<TagSubscription> list, IEnumerable<Tag> tags, Action<Entity> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var filter = new HashSet<Tag>(tags ?? Array.Empty<Tag>());
        var sub = new TagSubscription(filter, handler, list);
        list.Add(sub);
        return sub;
    }

    private static void DispatchTagSubscriptions(List<TagSubscription> list, Entity entity)
    {
        if (entity == null || entity.Tags.Count == 0) return;
        // iterate tags once; deliver to subs whose filter intersects
        foreach (var sub in list.ToArray())
        {
            if (sub.IsDisposed) continue;
            if (sub.Matches(entity.Tags))
            {
                sub.Handler?.Invoke(entity);
            }
        }
    }

    private sealed class TagSubscription : IDisposable
    {
        public readonly HashSet<Tag> Filter;
        public readonly Action<Entity> Handler;
        public bool IsDisposed { get; private set; }
        private readonly List<TagSubscription> _ownerList;

        public TagSubscription(HashSet<Tag> filter, Action<Entity> handler, List<TagSubscription> ownerList)
        {
            Filter = filter;
            Handler = handler;
            _ownerList = ownerList;
        }

        public bool Matches(IReadOnlyCollection<Tag> tags)
        {
            if (Filter.Count == 0) return false;
            foreach (var t in tags)
            {
                if (Filter.Contains(t)) return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _ownerList.Remove(this);
        }
    }
}

