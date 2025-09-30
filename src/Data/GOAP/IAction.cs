
namespace Game.Data.GOAP;

public enum ActionStatus { Running, Succeeded, Failed }
public enum ActionExitReason { Completed, Cancelled, Failed }

public interface IAction
{
    // Called once when the step begins. Must be idempotent (safe to call again after a Cancel).
    void Enter(RuntimeContext ctx);

    // Called every tick until it returns Succeeded or Failed.
    // No side-effects that would make re-Enter unsafe.
    ActionStatus Update(RuntimeContext ctx, float dt);

    // Always called once when the action finishes or gets preempted.
    // Clean up callbacks, reservations, particles, etc.
    void Exit(RuntimeContext ctx, ActionExitReason reason);

    // New: Log and report failure with reason
    void Fail(string reason);
}
