using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Utils;

namespace Game.Universe.Scenarios;

/// <summary>
/// A high-entity-count scenario for stress testing performance
/// </summary>
public class StressTestScenario : Scenario
{
	public override string Name => "Stress Test";
	public override string Description => "High entity count for performance testing (1000 NPCs, 3000 trees, 3000 food)";

	public override void Setup()
	{
		Log("Setting up stress test scenario...");

		// Spawn lots of trees
		for (int i = 0; i < (600 * 3); i++)
			SpawnEntity.Now(Nature.SimpleTree, Random.NextVector2(-20000, 20000));

		// Spawn lots of food
		for (int i = 0; i < (600 * 3); i++)
			SpawnEntity.Now(Food.RawBeef, Random.NextVector2(-20000, 20000));

		// Spawn many NPCs
		for (int i = 0; i < 600; i++)
			SpawnEntity.Now(NPC.Intelligent, Random.NextVector2(-5000, 5000));

		Log($"Spawned 7000 total entities");
	}
}
