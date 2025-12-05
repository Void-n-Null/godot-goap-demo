using Game.Data.GOAP;
using Game.Data.UtilityAI;

namespace Game.Data.Components;

#nullable enable

/// <summary>
/// Provides cached fact IDs for goal-related tags and agent status facts.
/// </summary>
public sealed class GoalFactRegistry
{
    public Tag[] TargetTags { get; }
    public string[] TargetTagStrings { get; }
    public int[] NearFactIds { get; }
    public int[] WorldCountFactIds { get; }
    public int[] WorldHasFactIds { get; }
    public int[] AgentCountFactIds { get; }
    public int[] AgentHasFactIds { get; }
    public int[] DistanceFactIds { get; }
    public int HungerFactId { get; }
    public int IsHungryFactId { get; }
    public int SleepinessFactId { get; }
    public int IsSleepyFactId { get; }

    public GoalFactRegistry()
    {
        TargetTags = Tags.TargetTags;
        TargetTagStrings = new string[TargetTags.Length];
        NearFactIds = new int[TargetTags.Length];
        WorldCountFactIds = new int[TargetTags.Length];
        WorldHasFactIds = new int[TargetTags.Length];
        AgentCountFactIds = new int[TargetTags.Length];
        AgentHasFactIds = new int[TargetTags.Length];
        DistanceFactIds = new int[TargetTags.Length];
        HungerFactId = FactRegistry.GetId("Hunger");
        IsHungryFactId = FactRegistry.GetId("IsHungry");
        SleepinessFactId = FactRegistry.GetId("Sleepiness");
        IsSleepyFactId = FactRegistry.GetId("IsSleepy");

        for (int i = 0; i < TargetTags.Length; i++)
        {
            string tagName = TargetTags[i].ToString();
            TargetTagStrings[i] = tagName;
            var tag = TargetTags[i];
            NearFactIds[i] = FactRegistry.GetId(FactKeys.NearTarget(tag));
            WorldCountFactIds[i] = FactRegistry.GetId(FactKeys.WorldCount(tag));
            WorldHasFactIds[i] = FactRegistry.GetId(FactKeys.WorldHas(tag));
            AgentCountFactIds[i] = FactRegistry.GetId(FactKeys.AgentCount(tag));
            AgentHasFactIds[i] = FactRegistry.GetId(FactKeys.AgentHas(tag));
            DistanceFactIds[i] = FactRegistry.GetId($"Distance_To_{tag}");
        }
    }
}

