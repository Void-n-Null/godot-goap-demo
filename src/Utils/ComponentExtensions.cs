using Game.Data.Components;

namespace Game.Utils;

public static class ComponentExtensions {
    public static T GetComponent<T>(this IComponent component) where T : class, IComponent {
        return component.Entity.GetComponent<T>();
    }
}