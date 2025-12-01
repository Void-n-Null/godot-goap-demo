using Game.Universe;
using Game.Utils;

namespace Game.Data.GOAP;

/// <summary>
/// Central service locator for accessing global game systems.
/// Provides convenient access to singleton managers and services.
/// </summary>
public static class ServiceLocator
{
    public static EntityManager EntityManager => EntityManager.Instance;
    public static GameManager GameManager => GameManager.Instance;
    public static EntityRenderEngine Renderer => EntityRendererFinder.Renderer;
}
