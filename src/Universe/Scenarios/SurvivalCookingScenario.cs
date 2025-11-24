using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Data.Components;
using Game.Data;
using Game.Utils;
using Godot;

namespace Game.Universe.Scenarios;

/// <summary>
/// A challenging survival scenario where an NPC must cook food to survive.
/// Contains: 1 NPC (Hungry, other stats full), 1 Tree, 1 Raw Beef.
/// </summary>
public class SurvivalCookingScenario : Scenario
{
    public override string Name => "Survival Cooking";
    public override string Description => "1 Starving NPC, 1 Tree, 1 Raw Beef. Cook to survive!";
    public override string Entities => "1 NPC, 1 Tree, 1 Raw Beef";

    public override void Setup()
    {
        Log("Setting up Survival Cooking scenario...");

        // 1. Spawn 1 Tree nearby
        SpawnEntity.Now(Nature.SimpleTree, new Vector2(200, 0));

        // 2. Spawn 3 Raw Beef nearby
        for (int i = 0; i < 3; i++)     
        {
            SpawnEntity.Now(Food.RawBeef, Random.NextVector2(-200, 200));
        }

        // 3. Spawn 1 NPC at center
        var npcEntity = SpawnEntity.Now(NPC.Intelligent, new Vector2(-200, 0));

        // 4. Configure NPC stats:
        // - Starving (Hunger = 100 or close to max if max is 100, let's say 80 to trigger goal)
        // - Not thirsty, not sleepy
        if (npcEntity.TryGetComponent<NPCData>(out var data))
        {
            data.Hunger = 90f;   // Very hungry
            data.Thirst = 0f;    // Not thirsty
            data.Sleepiness = 0f;// Not sleepy
            data.Temperature = 100f; // Warm enough to not be cold
            
            Log("NPC configured: Hungry=90, Thirst=0, Sleep=0, Temperature=100");
        }
    }
}

