using Game.Data;
using Godot;
using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Data.Components;

namespace Game.Universe.Scenarios;

/// <summary>
/// Focused harness for StayWarmGoal: two freezing NPCs, one tree of sticks, no pre-existing heat sources.
/// Forces them to chop the lone tree, gather sticks, and build/approach a campfire.
/// </summary>
public sealed class StayWarmTestScenario : Scenario
{
    private const float InitialTemperature = 5f;
    private static readonly bool UseTreeHeatSource = true; // Flip to false to spawn loose sticks instead
    private static readonly Vector2 TreePosition = new(0f, 350f);
    private static readonly Vector2[] StickOffsets =
    {
        new(-40f, 0f),
        new(40f, 0f),
        new(0f, -40f),
        new(0f, 40f)
    };
    private static readonly Vector2 AgentOnePosition = new(-150f, 0f);
    private static readonly Vector2 AgentTwoPosition = new(150f, 0f);

    public override string Name => "StayWarmTest";
    public override string Description => "Two cold agents race to gather sticks from a single tree and warm up.";
    public override string Entities => "2 NPCs, 1 Tree";

    public override void Setup()
    {
        Log("Setting up StayWarm cold-start scenario");

        SpawnResourceSource();

        SpawnColdAgent("Freezing NPC A", AgentOnePosition);
        SpawnColdAgent("Freezing NPC B", AgentTwoPosition);

        Log("StayWarm test scenario ready");
    }

    private static Entity SpawnColdAgent(string name, Vector2 position)
    {
        var entity = SpawnEntity.Now(NPC.Intelligent, position);
        entity.Name = name;

        if (entity.TryGetComponent<NPCData>(out var npcData))
        {
            npcData.Temperature = InitialTemperature;
            npcData.Hunger = 0f;
            npcData.Thirst = 0f;
            npcData.Sleepiness = 0f;
            npcData.Resources.Clear();
        }

        return entity;
    }

    private static void SpawnResourceSource()
    {
        if (UseTreeHeatSource)
        {
            SpawnEntity.Now(Nature.SimpleTree, TreePosition);
        }
        else
        {
            foreach (var offset in StickOffsets)
            {
                SpawnEntity.Now(Items.Stick, TreePosition + offset);
            }
        }
    }
}
