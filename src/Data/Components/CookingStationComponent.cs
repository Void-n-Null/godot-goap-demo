using System;
using Game.Data.Blueprints;
using Godot;

namespace Game.Data.Components;

public class CookingStationComponent : IActiveComponent
{
    public Entity Entity { get; set; }
    
    public bool IsCooking { get; private set; }
    public bool HasCookedItem { get; private set; }
    
    public EntityBlueprint InputBlueprint { get; private set; }
    public EntityBlueprint OutputBlueprint { get; private set; }
    
    public float CookingTimeRequired { get; set; } = 5.0f;
    public float CurrentCookingTimer { get; private set; }
    
    public event Action<CookingStationComponent> OnCookingComplete;
    
    public void StartCooking(EntityBlueprint input, EntityBlueprint output, float time)
    {
        if (IsCooking || HasCookedItem) return;
        
        InputBlueprint = input;
        OutputBlueprint = output;
        CookingTimeRequired = time;
        CurrentCookingTimer = 0f;
        IsCooking = true;
        HasCookedItem = false;
        
        // Optional: Visual updates could go here
    }
    
    public EntityBlueprint RetrieveItem()
    {
        if (!HasCookedItem) return null;
        
        var output = OutputBlueprint;
        Reset();
        return output;
    }
    
    private void Reset()
    {
        IsCooking = false;
        HasCookedItem = false;
        InputBlueprint = null;
        OutputBlueprint = null;
        CurrentCookingTimer = 0f;
    }

    public void OnPreAttached() { }
    public void OnPostAttached() { }
    public void OnStart() { }
    public void OnDetached() { }

    public void Update(double delta)
    {
        if (IsCooking)
        {
            CurrentCookingTimer += (float)delta;
            if (CurrentCookingTimer >= CookingTimeRequired)
            {
                IsCooking = false;
                HasCookedItem = true;
                OnCookingComplete?.Invoke(this);
            }
        }
    }
}

