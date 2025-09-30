using System.Collections.Generic;
using Game.Data;
using Game.Data.Components;

namespace Game.Data;

public class NPCData : IComponent
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
    
}