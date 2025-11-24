using Godot;
using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Data.Components;
using Game.Universe;
using Game.Data;

namespace Game.Universe.Scenarios;

/// <summary>
/// Scenario to test sleep goal and bed crafting.
/// One NPC, very tired, with enough sticks nearby to craft a bed.
/// </summary>
public sealed class SleepGoalTestScenario : Scenario
{
    public override string Name => "SleepGoalTest";
    public override string Description => "One exhausted NPC with 5 sticks nearby to test bed crafting and sleeping.";
    public override string Entities => "1 NPC, 5 Sticks";

    public override void Setup()
    {
        GD.Print("[Scenario:SleepGoalTest] Setting up...");

        // Spawn one intelligent NPC
        var npc = SpawnEntity.Now(NPC.Intelligent, new Vector2(0, 0));
        if (npc.TryGetComponent<NPCData>(out var data))
        {
            data.Sleepiness = 95f; // Very tired, should trigger sleep goal immediately
            data.Hunger = 0f;
            data.Thirst = 0f;
            data.Temperature = 70f; // Comfortable
        }

        // Spawn 5 sticks in a line in front of the NPC
        // Bed requires 4 sticks
        for (int i = 0; i < 6; i++)
        {
            SpawnEntity.Now(Items.Stick, new Vector2(100 + (i * 50), 0));
        }

        GD.Print("[Scenario:SleepGoalTest] Ready");
    }
}

