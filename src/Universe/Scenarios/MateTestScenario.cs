using Godot;
using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Data.Components;
using Game.Universe;
using Game.Data;
namespace Game.Universe.Scenarios;

public sealed class MateTestScenario : Scenario
{
	public override string Name => "MateTest";
	public override string Description => "Pairs of adult NPCs near each other to test the mating utility.";

	public override void Setup()
	{
		GD.Print("[Scenario:MateTest] Setting up mating test scenario...");

		SpawnEntity.Now(Campfire.SimpleCampfire, new Vector2(0, -25));

		var positions = new[]
		{
			new Vector2(-300, 0),
			new Vector2(-150, 0),
			new Vector2(150, 0),
			new Vector2(300, 0)
		};

		for (int i = 0; i < positions.Length; i++)
		{
			var npc = SpawnEntity.Now(NPC.Intelligent, positions[i]);
			if (npc.TryGetComponent<NPCData>(out var data))
			{
				data.ClearMateCooldown();
				data.MatingDesire = 95f;
				data.Gender = (i % 2 == 0) ? NPCGender.Male : NPCGender.Female;
				data.AgeGroup = NPCAgeGroup.Adult;
				data.Temperature = 80f;
				data.Hunger = 0f;
				data.Thirst = 0f;

				if (npc.TryGetComponent<VisualComponent>(out var visual))
				{
					var path = NPC.DetermineSpritePath(data);
					visual.SetSprite(path);
				}
			}
		}

		GD.Print("[Scenario:MateTest] Ready");
	}
}

