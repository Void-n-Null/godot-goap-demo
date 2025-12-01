using Game.Data;
using Game.Data.Blueprints.Objects;
using Game.Data.Blueprints;
using Game.Universe;
using Game.Utils;
using Godot;

namespace Game.Universe.Scenarios;

public class ThreePeopleManyFood : Scenario
{
    public override string Name => "3 People, Many Food";
    public override string Description => "3 Agents, 3 Trees, and abundant food to test proximity logic.";
    public override string Entities => "3 Agents, 3 Trees, 50 Food";

    public override void Setup()
    {
        // Spawn 3 Agents
        SpawnEntity.Now(NPC.Intelligent, new Vector2(-100, 0));
        SpawnEntity.Now(NPC.Intelligent, new Vector2(0, 0));
        SpawnEntity.Now(NPC.Intelligent, new Vector2(100, 0));

        // Spawn 3 Trees
        SpawnEntity.Now(Nature.BaseTree, new Vector2(-300, -300));
        SpawnEntity.Now(Nature.BaseTree, new Vector2(300, -300));
        SpawnEntity.Now(Nature.BaseTree, new Vector2(0, 300));

        // Spawn Many Food (50 RawBeef)
        for (int i = 0; i < 50; i++)
        {
            SpawnEntity.Now(Food.RawBeef, Random.NextVector2(-1000, 1000));
        }
    }
}
