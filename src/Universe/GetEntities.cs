using Game.Universe;
using Game.Data;
using System.Collections.Generic;
using System.Linq;
using Game.Data.Components;
using Godot;

namespace Game.Utils;

public static class GetEntities
{
    public static EntityManager EntityManager => EntityManager.Instance;
    private static SpatialIndex Spatial => EntityManager.SpatialPartition;

    public static IEnumerable<Entity> All()
    {
        return [.. EntityManager.AllEntities.OfType<Entity>()];
    }

    public static IEnumerable<Entity> OfBlueprint(EntityBlueprint blueprint)
    {
        if (TryGetQueryOrigin(out var position))
        {
            return Spatial.QueryCircle(position, float.PositiveInfinity, e => e.Blueprint == blueprint);
        }
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.Blueprint == blueprint)];
    }

    public static IEnumerable<Entity> WithComponent<T>() where T : class, IComponent
    {
        if (TryGetQueryOrigin(out var position))
        {
            return Spatial.QueryCircle(position, float.PositiveInfinity, e => e.GetComponent<T>() != null);
        }
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.GetComponent<T>() != null)];
    }

    public static IEnumerable<Entity> WithComponent<T>(T component) where T : class, IComponent
    {
        if (TryGetQueryOrigin(out var position))
        {
            return Spatial.QueryCircle(position, float.PositiveInfinity, e => e.GetComponent<T>() == component);
        }
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.GetComponent<T>() == component)];
    }

    public static IEnumerable<Entity> OfTag(Tag tag)
    {
        if (TryGetQueryOrigin(out var position))
        {
            return Spatial.QueryCircle(position, float.PositiveInfinity, e => e.Tags.Contains(tag));
        }
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.Tags.Contains(tag))];
    }

    public static IEnumerable<Entity> OfTag(string tagName)
    {
        var tag = Tag.From(tagName);
        return OfTag(tag);
    }

    public static IEnumerable<Entity> ContainingTags(params Tag[] tags)
    {
        if (TryGetQueryOrigin(out var position))
        {
            return Spatial.QueryCircle(position, float.PositiveInfinity, e => tags.All(e.Tags.Contains));
        }
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => tags.All(e.Tags.Contains))];
    }

    public static IEnumerable<Entity> ContainingTags(params string[] tagNames)
    {
        var tags = tagNames.Select(Tag.From).ToArray();
        return ContainingTags(tags);
    }

    // ---------------- InRange helpers ----------------
    public static IEnumerable<Entity> InRangeByTag(Tag tag, Vector2 center, float radius, int maxResults = int.MaxValue)
    {
        return Spatial.QueryCircle(center, radius, e => e.Tags.Contains(tag), maxResults);
    }

    public static IEnumerable<Entity> InRangeByComponent<T>(Vector2 center, float radius, int maxResults = int.MaxValue) where T : class, IComponent
    {
        return Spatial.QueryCircle(center, radius, e => e.GetComponent<T>() != null, maxResults);
    }

    public static IEnumerable<Entity> InRangeByPredicate(Vector2 center, float radius, System.Func<Entity, bool> predicate, int maxResults = int.MaxValue)
    {
        return Spatial.QueryCircle(center, radius, predicate, maxResults);
    }

    // ---------------- MultiTry utilities ----------------
    // Scales radius additively: startRadius + (step * i) for i in [0, attempts)
    public static IEnumerable<Entity> MultiTryInRangeByTag(Tag tag, Vector2 center, int attempts, float step, float startRadius = 0f, int maxResults = int.MaxValue)
    {
        for (int i = 0; i < attempts; i++)
        {
            var radius = startRadius + step * i;
            var found = Spatial.QueryCircle(center, radius, e => e.Tags.Contains(tag), maxResults);
            if (found != null && found.Count > 0)
                return found;
        }
        return System.Array.Empty<Entity>();
    }

    public static IEnumerable<Entity> MultiTryInRangeByComponent<T>(Vector2 center, int attempts, float step, float startRadius = 0f, int maxResults = int.MaxValue) where T : class, IComponent
    {
        for (int i = 0; i < attempts; i++)
        {
            var radius = startRadius + step * i;
            var found = Spatial.QueryCircle(center, radius, e => e.GetComponent<T>() != null, maxResults);
            if (found != null && found.Count > 0)
                return found;
        }
        return System.Array.Empty<Entity>();
    }

    public static IEnumerable<Entity> MultiTryInRangeByPredicate(Vector2 center, int attempts, float step, System.Func<Entity, bool> predicate, float startRadius = 0f, int maxResults = int.MaxValue)
    {
        for (int i = 0; i < attempts; i++)
        {
            var radius = startRadius + step * i;
            var found = Spatial.QueryCircle(center, radius, predicate, maxResults);
            if (found != null && found.Count > 0)
                return found;
        }
        return System.Array.Empty<Entity>();
    }

    private static bool TryGetQueryOrigin(out Vector2 origin)
    {
        // Prefer cached mouse location if available, useful for UI-driven queries
        if (ViewContext.CachedMouseGlobalPosition is Vector2 mouse)
        {
            origin = mouse;
            return true;
        }

        // Fallback: no origin context
        origin = default;
        return false;
    }
}