using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Utils;

namespace Game.Universe.Scenarios;

/// <summary>
/// A minimal scenario with just a few entities for quick testing
/// </summary>
public class MinimalScenario : Scenario
{
    public override string Name => "Minimal";
    public override string Description => "Small test scenario with 10 NPCs and basic resources";
    public override string Entities => "10 NPCs, 20 Trees, 20 Food";

    public override void Setup()
    {
        Log("Setting up minimal scenario...");

        // Spawn a few trees
        for (int i = 0; i < 20; i++)
            SpawnEntity.Now(Nature.SimpleTree, Random.NextVector2(-1000, 1000));

        // Spawn some food
        for (int i = 0; i < 20; i++)
            SpawnEntity.Now(Food.RawBeef, Random.NextVector2(-1000, 1000));

        // Spawn a handful of NPCs
        for (int i = 0; i < 10; i++)
            SpawnEntity.Now(NPC.Intelligent, Random.NextVector2(-500, 500));

        Log($"Spawned 50 total entities");
    }
}
