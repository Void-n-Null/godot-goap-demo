namespace Game.Data.NPCActions;

public enum ActionStatus { Running, Succeeded, Failed }
public enum ActionExitReason { Completed, Cancelled, Failed }

public interface IAction
{
    // Called once when the step begins. Must be idempotent (safe to call again after a Cancel).
    void Enter(Entity actor);

    // Called every tick until it returns Succeeded or Failed.
    // No side-effects that would make re-Enter unsafe.
    ActionStatus Update(Entity actor, float dt);

    // Always called once when the action finishes or gets preempted.
    // Clean up callbacks, reservations, particles, etc.
    void Exit(Entity actor, ActionExitReason reason);
}
