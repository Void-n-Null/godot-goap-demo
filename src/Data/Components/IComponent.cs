using Game.Data;

namespace Game.Data.Components;

// ===== COMPONENTS =====
// Two-Phase Attachment System:
//
// Phase 1 (PreAttached): Components are attached but cannot reference other components
// - Safe to setup component's own properties and basic initialization
// - Perfect for creating nodes, setting up internal state
// - Should not access other components (they might not be attached yet)
//
// Phase 2 (PostAttached): All components are attached to the entity, safe to reference others
// - Perfect for setting up inter-component references and dependencies
// - Components can query other components safely
// - Ideal for event subscriptions and cross-component setup
//
// Usage:
// 1. AddComponent() calls OnPreAttached() immediately
// 2. Call CompleteAttachment() after all components added
// 3. CompleteAttachment() calls OnPostAttached() for all components
//
// Benefits:
// - Eliminates race conditions during entity creation
// - Components can cache references to other components
// - No more "checking every frame if component exists"
// - Cleaner, more predictable initialization order

/// <summary>
/// Base component interface with two-phase attachment.
/// Use for metadata/data-only components that do not tick each frame.
/// </summary>
public interface IComponent
{
    Entity Entity { get; set; }

    /// <summary>
    /// Phase 1: Basic setup, component properties only.
    /// Safe to access component's own data and basic entity info.
    /// </summary>
    virtual void OnPreAttached() { }

    /// <summary>
    /// Phase 2: All components attached, safe to reference other components.
    /// Perfect for setting up inter-component references and dependencies.
    /// </summary>
    virtual void OnPostAttached() { }

    /// <summary>
    /// Called when component is detached from entity.
    /// Clean up references and event handlers here.
    /// </summary>
    virtual void OnDetached() { }
}

/// <summary>
/// Active components implement per-frame behavior and are updated by the Entity.
/// </summary>
public interface IActiveComponent : IComponent
{
    void Update(double delta);
}