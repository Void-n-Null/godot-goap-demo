using Game.Data.Components;
using Game.Data.GOAP;
using Godot;

namespace Game.Data.UtilityAI;

public class FindMateGoal : IUtilityGoal
{
	public string Name => "Find Mate";

	public float CalculateUtility(Entity agent)
	{
		if (!agent.TryGetComponent<NPCData>(out var data))
			return 0f;

		if (!data.ShouldSeekMate)
			return 0f;

		return Mathf.Clamp(data.MatingDesire / 100f, 0f, 1f);
	}

	public State GetGoalState(Entity agent)
	{
		var state = new State();
		state.Set(FactKeys.MateDesireSatisfied, true);
		return state;
	}

	public bool IsSatisfied(Entity agent)
	{
		if (!agent.TryGetComponent<NPCData>(out var data))
			return true;

		return data.MatingDesire < NPCData.MatingDesireThreshold * 0.5f || data.IsOnMateCooldown;
	}
}

