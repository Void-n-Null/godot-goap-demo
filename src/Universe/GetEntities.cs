using Game.Universe;
using Game.Data;
using System.Collections.Generic;
using System.Linq;
using Game.Data.Components;

namespace Game.Utils;

public static class GetEntities
{
    public static EntityManager EntityManager => EntityManager.Instance;
    public static IEnumerable<Entity> All()
    {
        return [.. EntityManager.AllEntities.OfType<Entity>()];
    }

    public static IEnumerable<Entity> OfBlueprint(EntityBlueprint blueprint)
    {
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.Blueprint == blueprint)];
    }

    public static IEnumerable<Entity> WithComponent<T>() where T : class, IComponent
    {
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.GetComponent<T>() != null)];
    }

    public static IEnumerable<Entity> WithComponent<T>(T component) where T : class, IComponent
    {
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.GetComponent<T>() == component)];
    }

    public static IEnumerable<Entity> OfTag(Tag tag)
    {
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.Tags.Contains(tag))];
    }

    public static IEnumerable<Entity> OfTag(string tagName)
    {
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => e.Tags.Contains(Tag.From(tagName)))];
    }

    public static IEnumerable<Entity> ContainingTags(params Tag[] tags)
    {
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => tags.All(t => e.Tags.Contains(t)))];
    }

    public static IEnumerable<Entity> ContainingTags(params string[] tagNames)
    {
        return [.. EntityManager.AllEntities.OfType<Entity>().Where(e => tagNames.All(t => e.Tags.Contains(Tag.From(t))))];
    }
}