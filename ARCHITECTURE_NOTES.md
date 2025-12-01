# Architecture Notes & Known Limitations

## Global Static State

### Problem
Several core systems use static singletons that make testing difficult and prevent running multiple game instances:

- `ComponentRegistry` - Static component type ID assignment
- `FactRegistry` - Static GOAP fact name → ID mapping  
- `Tag` registry - Static tag name → object mapping
- `AdvancedGoalPlanner._cachedFactories` - Static step factory cache

### Impact
- **Unit testing**: Cannot mock or isolate components in tests
- **Parallel tests**: Test suites cannot run in parallel without interference
- **Multiple instances**: Cannot run multiple game simulations simultaneously
- **Hot reload**: State persists across code reloads in editor

### Mitigation Strategy (For Future Refactoring)

To make registries instance-based:

1. **Create Registry Container**
   ```csharp
   public class GameRegistries
   {
       public ComponentRegistry Components { get; }
       public FactRegistry Facts { get; }
       public TagRegistry Tags { get; }
   }
   ```

2. **Thread Through Dependencies**
   - EntityFactory needs GameRegistries
   - Entity constructor needs ComponentRegistry
   - State constructor needs FactRegistry
   - Blueprint resolution needs TagRegistry

3. **Use Dependency Injection**
   - Pass GameRegistries to EntityManager
   - Store reference in Entity
   - Access via Entity.Registries.Components.GetId()

4. **Migration Path**
   - Add instance-based APIs alongside static ones
   - Gradually migrate call sites
   - Deprecate static APIs
   - Remove static state

### Estimated Effort
- 3-5 days of focused refactoring
- Requires touching ~30-40 files
- High risk of introducing bugs
- Needs comprehensive test coverage first

## EntityQuadTree Shared Buffer

### Problem
`QueryCircle()` and `QueryRectangle()` return a shared `List<Entity>` that is reused across calls.

### Mitigation
Clear documentation added with examples showing correct usage:
```csharp
// WRONG
var results = quadTree.QueryCircle(pos, radius);
// ... later use (corrupted by other queries)

// CORRECT  
var results = new List<Entity>(quadTree.QueryCircle(pos, radius));
```

### Why Not Fixed
The shared buffer is a deliberate performance optimization to avoid allocations in hot paths. Proper fix would be:
- Object pooling for result lists
- Span<T> return types (C# 12+)
- Caller-provided buffer pattern

## Entity Component Cache

### Fixed
Previously, `Entity.Transform` and `Entity.Visual` would permanently cache `null` if accessed before components were added. Now uses conditional caching that only stores non-null values.

## ResourceReservationManager

### Fixed
Previously had acknowledged bug where entity despawn didn't clear reservation cache. Now subscribes to `WorldEventBus.EntityDespawned` for automatic cleanup.

## Plan.Tick Dynamic Usage

### Fixed
Removed `dynamic` cast that caused runtime type resolution overhead in hot path. Now uses `FactValue.From()` static method.

## Two-Phase Component Attachment

### Current Design
Components have `OnPreAttached()` and `OnPostAttached()` lifecycle hooks to handle dependencies between components.

### Complexity
- Developers must understand when to use each hook
- Easy to get wrong (e.g., TransformComponent2D blocking pre-attachment modifications)
- Adds cognitive overhead

### Alternative Approaches
1. Unity-style `Awake()` / `Start()` with explicit dependency resolution
2. Single `OnAttached()` with automatic dependency ordering
3. Constructor injection with required component validation

### Recommendation
Document current patterns clearly rather than refactor mid-project. Consider simpler approach in future projects.

---

**Last Updated**: November 30, 2025  
**Reviewed By**: Architecture review session
