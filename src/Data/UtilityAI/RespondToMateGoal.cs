using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.UtilityAI;

public class RespondToMateGoal : IUtilityGoal
{
    public string Name => "Respond to Mate Request";

    public float CalculateUtility(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var data))
            return 0f;

        // High priority if we have a pending request
        if (data.IncomingMateRequestStatus == NPCData.MateRequestStatus.Pending)
        {
            return 0.95f; // Very high priority, interrupt other things
        }

        return 0f;
    }

    public State GetGoalState(Entity agent)
    {
        var state = new State();
        state.Set(FactKeys.MateRequestHandled, true);
        return state;
    }

    public bool IsSatisfied(Entity agent)
    {
        if (!agent.TryGetComponent<NPCData>(out var data))
            return true;

        return data.IncomingMateRequestStatus != NPCData.MateRequestStatus.Pending;
    }
}
