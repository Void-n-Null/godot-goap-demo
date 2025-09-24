using Game.Data;
using Game.Data.Components;

namespace Game.Data.Components;

public class FoodData : IComponent
{
    public EntityBlueprint CookedVariant { get; set; }
    public EntityBlueprint RawVariant { get; set; }

    public Entity Entity { get; set; }

    public bool IsCooked { get; set; }

    public float CookTime { get; set; }

    public float CookProgress { get; set; }

    public int HungerRestoredOnConsumption { get; set; }

    public int ThirstRestoredOnConsumption { get; set; }
    
    // Events
    public event System.Action<FoodData> FoodConsumed;
    
    public void MarkAsConsumed()
    {
        FoodConsumed?.Invoke(this);
    }
}