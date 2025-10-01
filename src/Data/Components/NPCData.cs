using System.Collections.Generic;
using Game.Data;
using Game.Data.Components;
using Godot;

namespace Game.Data;

public class NPCData : IActiveComponent
{
    const float DEFAULT_MAX = 100f;
    public Dictionary<TargetType, int> Resources { get; } = new Dictionary<TargetType, int>();
    public Entity Entity { get; set; }
    public string Name { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; } = DEFAULT_MAX;
    public float Hunger { get; set; } = 0f;
    public float MaxHunger { get; set; } = 100f;
    public float Thirst { get; set; }
    public float MaxThirst { get; set; } = DEFAULT_MAX;
    public float Sleepiness { get; set; }
    public float MaxSleepiness { get; set; } = DEFAULT_MAX;
    public float Happiness { get; set; }
    public float MaxHappiness { get; set; } = DEFAULT_MAX;
    public float Temperature { get; set; }
    

    public void Update(double delta)
    {
        // Lets look at the world! If were within 200 units of a heat source, we get warmer up to a max of 100, otherwise we get slowly colder!
        var heatSources = Game.Universe.EntityManager.Instance.QueryByTag(Tags.HeatSource, Entity.Transform.Position, 200f);
        foreach (var heatSource in heatSources)
        {
            Temperature += (float)delta * 10f;
        }

        Temperature -= (float)delta * 0.1f;
        Temperature = Mathf.Clamp(Temperature, 0f, 100f);

        // We slowly lose hunger and thirst and sleepiness
        Hunger -= (float)delta * 0.1f;
        Thirst -= (float)delta * 0.1f;
        Sleepiness -= (float)delta * 0.1f;

        Hunger = Mathf.Clamp(Hunger, 0f, MaxHunger);
        Thirst = Mathf.Clamp(Thirst, 0f, MaxThirst);
        Sleepiness = Mathf.Clamp(Sleepiness, 0f, MaxSleepiness);
    }
}