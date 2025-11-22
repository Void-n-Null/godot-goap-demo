using Game.Data;
using Game.Data.Blueprints.Objects;
using Game.Data.Blueprints;
using Game.Universe;
using Game.Utils;
using Godot;


namespace Game.Universe.Scenarios;

public class ManyFood : Scenario
{
    public override string Name => "Many Food";
    public override string Description => "Spawns a lot of food";
    public override string Entities => "150,000 Food";

    public override void Setup()
    {
        for (int i = 0; i < 150000; i++)
        {
            SpawnEntity.Now(Food.RawBeef, Random.NextVector2(-10000, 10000));
        }

        // SpawnEntity.Now(NPC.Intelligent, Vector2.Zero);
    }
}
