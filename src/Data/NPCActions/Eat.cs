// using System;

// namespace Game.Data.NPCActions;

// public sealed class Eat : IAction
// {
//     private readonly int _amount;
//     public Eat(int amount = 50) { _amount = amount; }

//     public void Enter(Entity actor)
//     {
//         var inv = actor.GetComponent<IInventoryComponent>();
//         if (inv?.Count("CookedMeal") > 0) inv.Consume("CookedMeal", 1);
//         else throw new InvalidOperationException("No meal"); // runner will treat as failure
//     }

//     public ActionStatus Update(Entity actor, float dt)
//     {
//         var needs = actor.GetComponent<INpcNeedsComponent>();
//         if (needs == null) return ActionStatus.Failed;
//         needs.Hunger = Math.Max(0, needs.Hunger - _amount);
//         return ActionStatus.Succeeded;
//     }

//     public void Exit(Entity actor, ActionExitReason reason) { /* no-op */ }
// }
