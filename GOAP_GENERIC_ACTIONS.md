# Generic GOAP Action System

## Problem
The original GOAP implementation had significant code repetition:
- **GoToFoodAction** and **GoToTargetAction** - both moved to entities
- **PickUpTargetAction**, **ChopTargetAction**, **ConsumeFoodAction** - all followed the same pattern
- Each action had its own StepFactory with nearly identical code
- Adding new actions required duplicating ~200+ lines of boilerplate

## Solution
A **parameterized, data-driven action system** that replaces hardcoded actions with configurable generic ones.

---

## Architecture

### 1. **EntityFinderConfig** - How to find targets
Defines criteria for finding entities to interact with:

```csharp
// Find food entities that are unreserved, and reserve them
var config = EntityFinderConfig.ByTargetType(
    TargetType.Food, 
    radius: 100f,
    requireUnreserved: true, 
    shouldReserve: true
);

// Find entities with HealthComponent that are alive
var config = EntityFinderConfig.ByComponent<HealthComponent>(
    comp => comp.IsAlive,
    radius: 64f
);
```

**Properties:**
- `Filter` - Predicate to match entities
- `SearchRadius` - How far to search
- `RequireReservation` - Must be reserved by this agent
- `RequireUnreserved` - Must not be reserved
- `ShouldReserve` - Reserve on Enter()

### 2. **InteractionEffectConfig** - What happens on completion
Defines the result of an interaction:

```csharp
// Pick up into inventory
var effect = InteractionEffectConfig.PickUp(TargetType.Wood);

// Kill entity (triggers loot drops)
var effect = InteractionEffectConfig.Kill();

// Consume food (reduces hunger)
var effect = InteractionEffectConfig.ConsumeFood();

// Custom effect
var effect = new InteractionEffectConfig
{
    OnComplete = (ctx, target) => 
    {
        // Your custom logic here
        GD.Print($"Interacted with {target.Name}");
    },
    DestroyTargetOnComplete = true,
    ReleaseReservationOnComplete = true
};
```

### 3. **MoveToEntityAction** - Generic movement
Replaces: GoToFoodAction, GoToTargetAction

```csharp
// Move to nearest unreserved food and reserve it
new MoveToEntityAction(
    EntityFinderConfig.ByTargetType(TargetType.Food, requireUnreserved: true, shouldReserve: true),
    reachDistance: 64f,
    actionName: "GoToFood"
);
```

### 4. **TimedInteractionAction** - Generic timed interaction
Replaces: PickUpTargetAction, ChopTargetAction, ConsumeFoodAction

```csharp
// Pick up wood (0.5s interaction)
new TimedInteractionAction(
    EntityFinderConfig.ByTargetType(TargetType.Wood, radius: 100f, requireReservation: true),
    interactionTime: 0.5f,
    InteractionEffectConfig.PickUp(TargetType.Wood),
    actionName: "PickUpWood"
);

// Chop tree (3s interaction)
new TimedInteractionAction(
    EntityFinderConfig.ByTargetType(TargetType.Tree, radius: 64f, requireReservation: true),
    interactionTime: 3.0f,
    InteractionEffectConfig.Kill(),
    actionName: "ChopTree"
);
```

### 5. **GenericStepFactory** - Unified step creation
Replaces: All individual StepFactory classes

The factory registers steps in `RegisterAllSteps()`:

```csharp
private void RegisterAllSteps()
{
    // Automatically create movement + pickup for all target types
    foreach (TargetType type in Enum.GetValues<TargetType>())
    {
        RegisterMoveToTargetStep(type);
        RegisterPickUpTargetStep(type);
    }

    // Add specialized steps
    RegisterChopTreeStep();
    RegisterConsumeFoodStep();
}
```

---

## Benefits

### ✅ Less Code
- **Before**: ~150 lines per action + 50 lines per factory = 200+ lines per behavior
- **After**: ~5 lines to configure a new action in GenericStepFactory

### ✅ Easier to Extend
Want to add a "Mine Rock" action? Just add:

```csharp
private void RegisterMineRockStep()
{
    var config = new StepConfig("MineRock")
    {
        ActionFactory = () => new TimedInteractionAction(
            EntityFinderConfig.ByTargetType(TargetType.Rock, radius: 64f, requireReservation: true),
            interactionTime: 4.0f,
            InteractionEffectConfig.Kill(), // Spawns ore via loot drop
            actionName: "MineRock"
        ),
        Preconditions = new Dictionary<string, object>
        {
            { FactKeys.NearTarget(TargetType.Rock), true }
        },
        Effects = new Dictionary<string, object>
        {
            { FactKeys.TargetChopped(TargetType.Rock), true }
        },
        CostFactory = _ => 4.0
    };
    _stepConfigs.Add(config);
}
```

### ✅ Composable and Reusable
Mix and match finder configs, effects, and timings:

```csharp
// Fast pickup of nearby unreserved items
new TimedInteractionAction(
    EntityFinderConfig.ByComponent<TargetComponent>(tc => tc.Target == TargetType.Gold, 50f, true, false),
    0.1f,
    InteractionEffectConfig.PickUp(TargetType.Gold),
    "QuickGrab"
);
```

### ✅ Data-Driven
Could load step configs from JSON/YAML instead of hardcoding them.

---

## Migration Guide

### Old Way (Hardcoded)
```csharp
// GoToFoodAction.cs (~96 lines)
public sealed class GoToFoodAction : IAction, IRuntimeGuard { ... }

// GoToFoodStepFactory.cs (~44 lines)
[StepFactory]
public class GoToFoodStepFactory : IStepFactory { ... }
```

### New Way (Parameterized)
```csharp
// In GenericStepFactory.RegisterAllSteps()
RegisterMoveToTargetStep(TargetType.Food); // Creates GoTo_Food step
```

---

## Future Enhancements

1. **External Configuration**: Load steps from JSON/scriptable objects
2. **Validation Callbacks**: Add runtime validation for proximity, components
3. **Composite Actions**: Chain multiple actions together
4. **State Mutations**: Handle inventory count increments in effects
5. **Action Decorators**: Add logging, profiling, debugging wrappers

---

## Files

### New Files
- `src/Data/GOAP/GenericActions/ActionParameters.cs` - Config classes
- `src/Data/GOAP/GenericActions/MoveToEntityAction.cs` - Generic movement
- `src/Data/GOAP/GenericActions/TimedInteractionAction.cs` - Generic interaction
- `src/Data/GOAP/GenericActions/GenericStepFactory.cs` - Unified factory

### Can Be Deleted (After Migration)
- `GoToFoodAction.cs` + `GoToFoodStepFactory.cs`
- `GoToTargetAction.cs` + `GoToTargetStepFactory.cs`
- `PickUpTargetAction.cs` + `PickUpTargetStepFactory.cs`
- `ChopTargetAction.cs` + `ChopTargetStepFactory.cs`
- `ConsumeFoodAction.cs` + `ConsumeFoodStepFactory.cs`

**Total reduction**: ~600+ lines of repetitive code replaced by ~400 lines of reusable abstractions
