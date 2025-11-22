using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Utils;

namespace Game.Universe.Scenarios;

/// <summary>
/// The default game scenario with a balanced mix of entities
/// </summary>
public class DefaultScenario : Scenario
{
    public override string Name => "Default";
    public override string Description => "Balanced starting scenario with 500 NPCs, 1500 trees, and 1500 food items";
    public override string Entities => "500 NPCs, 1500 Trees, 1500 Food";

    public override void Setup()
    {
        Log("Setting up default scenario...");

        // Spawn trees for wood gathering
        for (int i = 0; i < 1500; i++)
            SpawnEntity.Now(Nature.SimpleTree, Random.NextVector2(-15000, 15000));

        // Spawn food for eating
        for (int i = 0; i < 1500; i++)
            SpawnEntity.Now(Food.RawBeef, Random.NextVector2(-15000, 15000));

        // Spawn NPCs with randomized hunger
        for (int i = 0; i < 500; i++)
            SpawnEntity.Now(NPC.Intelligent, Random.NextVector2(-3000, 3000));

        Log($"Spawned 3500 total entities");
    }
}
