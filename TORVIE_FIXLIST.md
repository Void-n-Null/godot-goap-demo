### Torvie Fix List — Non‑Negotiable Changes

This is the hard list. Ship these fixes before adding features. Each item includes the why, the exact changes to make, and acceptance criteria.

---

### 1) Component replacement must be safe and deterministic

- **Why**: Adding a component of the same runtime type silently overwrites the old one without calling `OnDetached()`. That leaks nodes and event handlers, and leaves `_activeComponents` inconsistent.
- **Files**: `src/Data/Entity.cs`
- **Required edits**:
  - Add a private initialization flag: `private bool _isInitialized;`
  - In `Initialize()`, set `_isInitialized = true` before completing attachment.
  - In both overloads of `AddComponent(...)`:
    - If a component with the same runtime key exists, call `OnDetached()`, clear its `Entity`, and remove it from `_activeComponents` if applicable.
    - After setting `Entity` and calling `OnPreAttached()`, if `_isInitialized` is true, call `OnPostAttached()` immediately and add to `_activeComponents` if `IActiveComponent`.
  - In `RemoveComponent<T>()`, also remove from `_activeComponents` if the removed component implements `IActiveComponent`.
- **Code-level directive (pattern)**:
```csharp
// Entity.cs
private bool _isInitialized;

public void Initialize()
{
    _isInitialized = true;
    CompleteAttachment();
}

public void AddComponent(IComponent component)
{
    var key = component.GetType();
    if (_components.TryGetValue(key, out var existing))
    {
        if (existing is IActiveComponent existingActive) _activeComponents.Remove(existingActive);
        existing.OnDetached();
        existing.Entity = null;
    }

    _components[key] = component;
    component.Entity = this;
    component.OnPreAttached();

    if (_isInitialized)
    {
        component.OnPostAttached();
        if (component is IActiveComponent active) _activeComponents.Add(active);
    }
}

public bool RemoveComponent<T>() where T : IComponent
{
    if (_components.TryGetValue(typeof(T), out var component))
    {
        component.OnDetached();
        component.Entity = null;
        if (component is IActiveComponent active) _activeComponents.Remove(active);
        return _components.Remove(typeof(T));
    }
    return false;
}
```
- **Acceptance**:
  - Replacing a `VisualComponent` doesn’t leak old nodes (no stray views, no double updates).
  - Adding a component after `Initialize()` immediately wires `OnPostAttached()` and starts updating if active.

---

### 2) Make retrieval semantics explicit (exact type vs. polymorphic)

- **Why**: Components are keyed by runtime type; `GetComponent<T>()` uses `typeof(T)`. Interface queries won’t find implementations. Either enforce exact-type access or support polymorphic lookups.
- **Files**: `src/Data/Entity.cs`
- **Required edits (choose one)**:
  - Enforce exact-type semantics: document it on `GetComponent<T>()` and `HasComponent<T>()`, and add a debug assert/log when generic `T` is an interface/abstract type.
  - Or support polymorphic fallbacks: when exact key miss, scan `_components.Values` for `is T` and return the first.
- **Acceptance**:
  - Behavior is documented and consistent; tests cover interface lookup behavior.

---

### 3) Consistent transform sync in `VisualComponent`

- **Why**: Mixing global position/rotation with local scale is inconsistent and creates warped transforms under parents.
- **Files**: `src/Data/Components/VisualComponent.cs`
- **Required edits**:
  - Use local space consistently for all three: `Position`, `Rotation`, `Scale`.
  - Apply the same in the initial sync inside `OnPostAttached()`.
- **Code-level directive**:
```csharp
// VisualComponent.Update
ViewNode.Position = _transform2D.Position;
ViewNode.Rotation = _transform2D.Rotation;
ViewNode.Scale = _transform2D.Scale;

// VisualComponent.OnPostAttached initial sync matches the above
```
- **Acceptance**:
  - Parenting a view under a transformed node preserves expected local transforms without skewing.

---

### 4) Remove view defaults from data components

- **Why**: `TransformComponent2D` pulls `ViewContext.DefaultScale`, coupling data to view. Data must be pure.
- **Files**: `src/Data/Components/TransformComponent2D.cs`, `src/Utils/ViewContext.cs`
- **Required edits**:
  - In `TransformComponent2D`, default `Scale` to `Vector2.One` and remove any reference to `ViewContext`.
  - If you want default visual scaling, apply it in `VisualComponent` when creating/attaching the view.
- **Code-level directive**:
```csharp
// TransformComponent2D
public class TransformComponent2D(Vector2 Position = default, float Rotation = default, Vector2? Scale = null) : IComponent
{
    public Vector2 Position { get; set; } = Position;
    public float Rotation { get; set; } = Rotation;
    public Vector2 Scale { get; set; } = Scale ?? Vector2.One;
    public Entity Entity { get; set; }
}
```
- **Acceptance**:
  - Data layer compiles without referencing `Game.Utils`.

---

### 5) Blueprint duplication policy and leak prevention

- **Why**: Layered blueprints can emit multiple components of the same runtime type. With current semantics, the earlier one runs `OnPreAttached()` and then is overwritten, leaking side effects.
- **Files**: `src/Data/EntityBlueprint.cs`, `src/Data/Entity.cs`
- **Required edits**:
  - Keep the safe-replacement logic from Fix #1 to avoid leaks even pre-initialization.
  - Add a validation pass when materializing components (or in `EntityBlueprint.CreateAllComponents`) to detect duplicate runtime types across the chain and log a warning.
- **Code-level directive (validation sketch)**:
```csharp
// Inside EntityBlueprint.CreateAllComponents or during From(...)
var seen = new HashSet<Type>();
foreach (var bp in EnumerateRootToLeaf())
{
    var comps = bp.ComponentsFactory?.Invoke();
    if (comps == null) continue;
    foreach (var c in comps)
    {
        var t = c.GetType();
        if (!seen.Add(t)) GD.PushWarning($"Duplicate component type {t.Name} in blueprint chain '{Name}'. Last one wins.");
        yield return c;
    }
}
```
- **Acceptance**:
  - Duplicate types don’t leak and emit a single clear warning.

---

### 6) Add `Despawn` that actually cleans up entities

- **Why**: Unregistering without destroying leaks view nodes and event handlers.
- **Files**: `src/Universe/EntityManager.cs`
- **Required edits**:
  - Implement `public void Despawn(Entity entity)` that calls `UnregisterEntity(entity)` and then `entity.Destroy()`.
  - Use this path whenever removing entities.
- **Code-level directive**:
```csharp
public void Despawn(Entity entity)
{
    if (entity == null) return;
    UnregisterEntity(entity);
    entity.Destroy();
}
```
- **Acceptance**:
  - After despawn, no view nodes remain in the scene tree, and ticks stop.

---

### 7) Decouple movement from input/global context

- **Why**: `MovementComponent` reads the mouse via `ViewContext`. That conflates input and movement logic, making it untestable and brittle.
- **Files**: `src/Data/Components/MovementComponent.cs`
- **Required edits**:
  - Replace direct mouse reads with a simple contract: move toward a `TargetPosition` when set, otherwise do nothing.
  - Expose `public Vector2? TargetPosition { get; set; }` and drive movement purely from that.
  - Have some system (e.g., a demo controller) update `TargetPosition` based on input.
- **Code-level directive (core loop)**:
```csharp
public Vector2? TargetPosition { get; set; }

public void Update(double delta)
{
    if (_transform2D == null || TargetPosition is null) return;
    var toTarget = TargetPosition.Value - _transform2D.Position;
    if (toTarget.Length() <= 0.001f) { Velocity = Vector2.Zero; return; }
    var direction = toTarget.Normalized();
    Velocity = direction * MaxSpeed;
    _transform2D.Position += Velocity * (float)delta;
}
```
- **Acceptance**:
  - Movement follows externally supplied targets; no direct Godot input or global context usage inside the component.

---

### 8) Resource loader: add basic error handling and caching

- **Why**: Blind loads without checks spam the loader and hide errors.
- **Files**: `src/Utils/Resources.cs`
- **Required edits**:
  - Add a simple cache dictionary keyed by `(Type,path)`.
  - Normalize `res://` but don’t prepend if the path already starts with `res://` or `user://`.
  - Log a warning if `GD.Load<T>(path)` returns null.
- **Code-level directive (sketch)**:
```csharp
private static readonly Dictionary<(Type,string), Resource> _cache = new();

private static T GetResource<T>(string path) where T : Resource
{
    if (!(path.StartsWith("res://") || path.StartsWith("user://"))) path = "res://" + path;
    var key = (typeof(T), path);
    if (_cache.TryGetValue(key, out var r)) return (T)r;
    var loaded = GD.Load<T>(path);
    if (loaded == null) GD.PushWarning($"Resources: Failed to load {typeof(T).Name} at {path}");
    else _cache[key] = loaded;
    return loaded;
}
```
- **Acceptance**:
  - Repeated loads hit the cache; bad paths produce a single, clear warning.

---

### 9) Health visual feedback: either wire it or remove it

- **Why**: `UpdateVisualHealthFeedback` computes a color but doesn’t apply it; dead code confuses readers.
- **Files**: `src/Data/Components/HealthComponent.cs`
- **Required edits**:
  - If a `Sprite2D` is available via `VisualComponent.Sprite`, apply `Sprite.Modulate = color;`, otherwise remove the method and leave a clear extension point comment.
- **Acceptance**:
  - Health changes visibly affect sprites or the method is gone.

---

### 10) Optional: simplify the singleton

- **Why**: Locking and BFS are heavyweight for Godot’s single-threaded node graph.
- **Files**: `src/Utils/SingletonNode2D.cs`
- **Required edits**:
  - Consider removing locks and preferring a simpler “first in scene wins” pattern. If you keep the current one, do not access `Instance` in constructors or `OnPreAttached`.
- **Acceptance**:
  - No exceptions thrown due to early `Instance` access during entity construction.

---

### Test Scenarios (must pass)

- **Replace Visual at runtime**: Add `VisualComponent`, call `Initialize()`, then add another `VisualComponent`. Old view is detached and freed; only the new view remains and updates.
- **Add Movement post-init**: Spawn an entity without movement, `Initialize()`, then add `MovementComponent`. It begins ticking immediately and moves toward provided `TargetPosition`.
- **Blueprint duplicate components**: Compose a derived blueprint that adds two visuals. Only the last one is active; a single warning is logged; no leaked nodes.
- **Despawn**: Spawn then despawn an entity. The manager stops ticking it; all component `OnDetached()` methods fire; view is removed from the scene tree.
- **Transform sync under parent**: Parent a view under a scaled/rotated node; visual correctly matches local position/rotation/scale from `TransformComponent2D` without shearing.

---

### Non‑Goals (don’t spend time here yet)

- Pathfinding, AI, or GOAP behaviors.
- Multi-scene streaming or pooling.
- Editor tooling for blueprints.

Do these fixes, then we can talk about adding brains.


