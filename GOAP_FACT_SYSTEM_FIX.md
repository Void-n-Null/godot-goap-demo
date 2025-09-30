# GOAP Fact System Fix - Planning Bug Resolved

## The Critical Bug

**Root Cause:** PickUpTargetStepFactory checked if resources exist in the **initial state** before creating pickup steps. This meant:

```
Initial State: Available_Stick=False (no sticks exist)
→ PickUpStick step NOT created
→ Planner has no way to pick up sticks
→ EVEN IF chopping creates sticks during planning!
```

### The Bug Flow:
1. Agent starts near tree (`Near_Tree=True`)
2. Trees exist (`World_Has_Tree=True`) 
3. **Sticks don't exist yet** (`World_Has_Stick=False`)
4. ChopTree step created ✓
5. **PickUpStick step NOT created** ❌ (checked initial state!)
6. Planner finds ChopTree step, applies it, creates sticks
7. But can't find PickUpStick step (was never generated)
8. **No plan possible!** ❌

## The Fix

### 1. **Created Centralized Fact System** (`FactKeys.cs`)
Clean, consistent fact naming using helper functions:

```csharp
// Agent facts
FactKeys.AgentHas(TargetType.Stick)     // "Has_Stick"
FactKeys.AgentCount(TargetType.Stick)   // "Agent_Stick_Count"
FactKeys.NearTarget(TargetType.Tree)    // "Near_Tree"

// World facts
FactKeys.WorldHas(TargetType.Stick)     // "World_Has_Stick"
FactKeys.WorldCount(TargetType.Stick)   // "World_Stick_Count"

// Action facts
FactKeys.TargetChopped(TargetType.Tree) // "Tree_Chopped"
```

**Benefits:**
- No more string typos
- IntelliSense autocomplete
- Easy refactoring
- Self-documenting

### 2. **Fixed Step Generation Logic**

**Before (BROKEN):**
```csharp
// Only create if exists in INITIAL state
if (!initialState.Facts.TryGetValue($"Available_{targetName}", ...) || !(bool)available)
{
    continue; // Skip - BUG!
}
```

**After (FIXED):**
```csharp
// ALWAYS create pickup steps - resources might become available during planning
var steps = new List<Step>();
foreach (TargetType type in Enum.GetValues<TargetType>())
{
    // Always create step, preconditions will gate it properly
    var preconds = new Dictionary<string, object>
    {
        { FactKeys.WorldHas(type), true },   // Precondition: must exist
        { FactKeys.NearTarget(type), true }  // Precondition: must be near
    };
    // ... create step
}
```

**Key Change:** Steps are created unconditionally, then **preconditions** determine when they're applicable.

### 3. **Updated All Step Factories**

#### ChopTargetStepFactory
- Preconditions: `Near_Tree=true`, `World_Has_Tree=true`
- Effects: Creates sticks, decrements trees, sets `World_Has_Stick=true`
- **Important:** Agent stays near tree (sticks drop at same location)

#### GoToTargetStepFactory  
- Preconditions: `World_Has_{Target}=true`
- Effects: `Near_{Target}=true`
- Always creates steps for all target types

#### PickUpTargetStepFactory
- Preconditions: `World_Has_{Target}=true`, `Near_{Target}=true`
- Effects: Increments `Agent_{Target}_Count`, decrements `World_{Target}_Count`
- **Important:** Doesn't set `Near_X=false` (multiple items at same location)

### 4. **Updated State Builder**

Cleaner fact generation using FactKeys:

```csharp
foreach (TargetType rt in Enum.GetValues<TargetType>())
{
    int worldCount = _worldData.EntityCounts.GetValueOrDefault(rt.ToString(), 0);
    int agentCount = npcData.Resources.GetValueOrDefault(rt, 0);
    
    // World facts
    facts[FactKeys.WorldCount(rt)] = worldCount;
    facts[FactKeys.WorldHas(rt)] = worldCount > 0;
    
    // Agent facts
    facts[FactKeys.AgentCount(rt)] = agentCount;
    facts[FactKeys.AgentHas(rt)] = agentCount > 0;
}

// Proximity facts
facts[FactKeys.NearTarget(targetType)] = nearbyCount > 0;
```

## Expected Plan Now

With the fix, the planner should find:

```
Initial State:
- Near_Tree=True (agent is near tree)
- World_Has_Tree=True (trees exist)
- World_Has_Stick=False (no sticks yet)
- Agent_Stick_Count=0 (no sticks in inventory)

Step 1: ChopTree
- Preconditions: Near_Tree=True ✓, World_Has_Tree=True ✓
- Effects: World_Has_Stick=True, World_Stick_Count=4

Step 2: PickUpStick (now available!)
- Preconditions: World_Has_Stick=True ✓, Near_Stick=True ✓ (at tree location)
- Effects: Agent_Stick_Count=1

Step 3-5: PickUpStick (repeat)
- Effects: Agent_Stick_Count=2, 3, 4

Step 6: ChopTree (if near another tree, or go to one)
- Effects: World_Stick_Count=8

... repeat until Agent_Stick_Count=12

Goal Satisfied! ✓
```

## Testing

Run the game and check logs. Should see:
```
Starting GOAP planning...
Found plan with X steps!
Plan: ChopTargetAction -> PickUpTargetAction -> ...
```

Instead of:
```
No plan found to get sticks, trying again later...
```

## Key Learnings

1. **Step generation must consider future states**, not just initial state
2. **Preconditions gate applicability**, not factory conditionals
3. **Centralized fact naming prevents bugs** and improves maintainability
4. **Clear fact semantics** (Has, Count, Near) make system understandable
