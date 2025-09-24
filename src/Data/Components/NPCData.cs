using Game.Data;
using Game.Data.Components;

public class NPCData : IComponent
{
    const float DEFAULT_MAX = 100f;
    public Entity Entity { get; set; }
    public string Name { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; } = DEFAULT_MAX;
    public float Hunger { get; set; }
    public float MaxHunger { get; set; } = DEFAULT_MAX;
    public float Thirst { get; set; }
    public float MaxThirst { get; set; } = DEFAULT_MAX;
    public float Sleepiness { get; set; }
    public float MaxSleepiness { get; set; } = DEFAULT_MAX;
    public float Happiness { get; set; }
    public float MaxHappiness { get; set; } = DEFAULT_MAX;
 
    
}