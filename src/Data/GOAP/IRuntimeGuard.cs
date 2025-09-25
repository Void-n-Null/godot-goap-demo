namespace Game.Data.NPCActions;

public interface IRuntimeGuard
{
    // If this flips to false mid-step (e.g., firepit stolen), the runner cancels + replans.
    bool StillValid(Entity actor);
}
