# GOAP System Refactor Summary

## Overview
Comprehensive refactor to fix architectural issues in the GOAP (Goal-Oriented Action Planning) system. The system now properly separates planning-time concerns from runtime execution.

## Key Problems Fixed

### 1. ✅ State vs Runtime Context Separation
**Problem:** State mixed abstract planning facts with runtime references (Agent, World)
**Solution:** 
- Created `RuntimeContext` class for runtime-only data (Agent, World)
- State is now pure facts only - no Agent or World references
- Planning operates on abstract state, execution uses RuntimeContext

### 2. ✅ Removed World Queries from Planning
**Problem:** Step factories and cost functions queried EntityManager during planning
**Solution:**
- Step factories now only check State.Facts
- Cost factories use state facts or fixed estimates
- No more `ctx.World.EntityManager` during planning phase

### 3. ✅ Standardized Action Pattern
**Problem:** Two inconsistent action patterns (GUID-based vs TargetType-based)
**Solution:**
- Removed GUID-based `GoToAction` and `GoToStepFactory`
- All actions now use `TargetType` enum approach
- Consistent pattern: actions find nearest target at runtime

### 4. ✅ Implemented IRuntimeGuard Checking
**Problem:** Runtime validation interface existed but was never called
**Solution:**
- Plan.Tick() now checks `IRuntimeGuard.StillValid()` each frame
- Plan automatically fails if guard returns false
- Enables reactive replanning when world state invalidates current action

### 5. ✅ Standardized Fact Naming Convention
**Problem:** Inconsistent fact names (AtTree vs At_Tree, StickCount vs Inventory_Stick_Count)
**Solution:**
- Consistent pattern: `At_{Target}`, `Available_{Target}`, `World_{Target}_Count`, `Inventory_{Target}_Count`
- All step factories and state builders use same convention
- Easy to trace facts through planning and execution

### 6. ✅ Removed Step Regeneration During Planning
**Problem:** Planner regenerated steps mid-search, but factories queried real world
**Solution:**
- Steps generated once from initial state facts
- No regeneration during planning
- Cleaner separation: planning = pure state transformation

## Updated Files

### Core GOAP System
- `RuntimeContext.cs` - NEW: Runtime execution context
- `State.cs` - Removed Agent/World properties
- `IAction.cs` - Uses RuntimeContext instead of State
- `IRuntimeGuard.cs` - Uses RuntimeContext
- `Step.cs` - Simplified action factory (no State parameter)
- `Plan.cs` - Uses RuntimeContext, implements IRuntimeGuard checking
- `GOAPlanner.cs` - Removed step regeneration, fixed CreateAction() calls

### Step Factories (Standardized)
- `ChopTargetStepFactory.cs` - Uses state facts only, standardized naming
- `GoToTargetStepFactory.cs` - Uses state facts only, standardized naming
- `PickUpTargetStepFactory.cs` - Uses state facts only, standardized naming

### Actions (Updated to RuntimeContext)
- `ChopTargetAction.cs` - Uses RuntimeContext
- `GoToTargetAction.cs` - Uses RuntimeContext
- `PickUpTargetAction.cs` - Uses RuntimeContext

### Behavior Controller
- `UtilityAIBehavior.cs` - Builds pure State, passes RuntimeContext to Plan.Tick(), uses standardized fact names

### Removed Files
- `GoToAction.cs` - DELETED (replaced by GoToTargetAction)
- `GoToStepFactory.cs` - DELETED (replaced by GoToTargetStepFactory)

## Fact Naming Standard

### Position Facts
- `At_{TargetType}` - Boolean, true if agent is near target (e.g., `At_Tree`, `At_Stick`)

### Availability Facts  
- `Available_{TargetType}` - Boolean, true if target exists in world (e.g., `Available_Tree`)

### Count Facts
- `World_{TargetType}_Count` - Integer, count of targets in world (e.g., `World_Tree_Count`)
- `Inventory_{TargetType}_Count` - Integer, count in agent inventory (e.g., `Inventory_Stick_Count`)

### Other Facts
- `{TargetType}_Chopped` - Boolean, target was chopped this step
- `Distance_To_{TargetType}` - Double, estimated distance (optional, for cost calculations)

## Architecture Flow

### Planning Phase (Pure State Transformation)
```
1. Build State from world observations (facts only)
2. GOAPlanner.Plan(initialState, goalState)
   - Generate all steps from initial state facts
   - A* search through abstract state space
   - No world queries, pure fact manipulation
3. Return Plan with sequence of Steps
```

### Execution Phase (Runtime Context)
```
1. Create RuntimeContext(agent, world)
2. For each step in plan:
   - Create action from step factory
   - action.Enter(runtimeCtx) - can query world
   - Loop: action.Update(runtimeCtx, dt)
   - Check IRuntimeGuard.StillValid(runtimeCtx)
   - action.Exit(runtimeCtx, reason)
   - Apply effects to plan state
```

## How to Add New Actions

1. **Create Action Class**
   ```csharp
   public class MyAction : IAction, IRuntimeGuard
   {
       public void Enter(RuntimeContext ctx) { /* query world */ }
       public ActionStatus Update(RuntimeContext ctx, float dt) { /* execute */ }
       public void Exit(RuntimeContext ctx, ActionExitReason reason) { /* cleanup */ }
       public bool StillValid(RuntimeContext ctx) { /* check validity */ }
       public void Fail(string reason) { /* log failure */ }
   }
   ```

2. **Create Step Factory**
   ```csharp
   [StepFactory]
   public class MyStepFactory : IStepFactory
   {
       public List<Step> CreateSteps(State initialState)
       {
           // Check state facts ONLY (no world queries!)
           if (!initialState.Facts.TryGetValue("Available_Thing", out var avail) || !(bool)avail)
               return new List<Step>();
           
           var step = new Step(
               actionFactory: () => new MyAction(), // No parameters from state
               preconditions: new Dictionary<string, object> { {"At_Thing", true} },
               effects: new Dictionary<string, object> { {"Thing_Used", true} },
               costFactory: state => 5.0 // Use state facts or fixed cost
           );
           return new List<Step> { step };
       }
   }
   ```

3. **Update State Builder (UtilityAIBehavior)**
   - Add facts for your new action's preconditions
   - Follow naming convention: `Available_{Thing}`, `At_{Thing}`, etc.

## Benefits

- ✅ Clean separation: Planning (abstract) vs Execution (concrete)
- ✅ Expandable: Add new actions by following clear patterns  
- ✅ Predictable: Consistent fact naming and data flow
- ✅ Robust: Runtime guards enable reactive replanning
- ✅ Efficient: No world queries during planning
- ✅ Debuggable: Clear where to look for planning vs execution issues
