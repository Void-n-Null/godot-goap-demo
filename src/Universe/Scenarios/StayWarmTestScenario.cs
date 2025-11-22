using Godot;
using Game.Data;
using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Data.Components;
using Game.Utils;

namespace Game.Universe.Scenarios;

/// <summary>
/// Minimal setup for debugging StayWarm/EatFood plans.
/// Spawns a single NPC near a small cluster of trees plus one food item.
/// </summary>
public sealed class StayWarmTestScenario : Scenario
{
    public override string Name => "StayWarmTest";
    public override string Description => "One NPC, three trees, and a single food item for focused GOAP debugging";
    public override string Entities => "2 NPCs, 3 Trees, 2 Food";

    public override void Setup()
    {
        Log("Setting up StayWarm test scenario...");

        // Spawn a tiny grove of trees around the origin
        var treePositions = new[]
        {
            new Vector2(-400, 0),
            new Vector2(400, 0),
            new Vector2(0, 450)
        };

        foreach (var pos in treePositions)
        {
            SpawnEntity.Now(Nature.SimpleTree, pos);
        }

        // SpawnEntity.Now(Campfire.SimpleCampfire, new Vector2(-600, 0));

        // Spawn a single food item close to the agent spawn
        SpawnEntity.Now(Food.RawBeef, new Vector2(-2200, -75));
        SpawnEntity.Now(Food.RawBeef, new Vector2(-2400, -75));
        // Spawn one intelligent NPC near the center
        SpawnEntity.Now(NPC.Intelligent, new Vector2(-3000, 0));
        SpawnEntity.Now(NPC.Intelligent, new Vector2(600, 0)); //Far enough that he wont be able to make his own fire in time.

        Log("StayWarm test scenario ready");
    }
}
