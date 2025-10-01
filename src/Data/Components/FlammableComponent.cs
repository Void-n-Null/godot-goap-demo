using Godot;
using Game.Data;
using Game.Utils;
using System.Collections.Generic;

namespace Game.Data.Components;

/// <summary>
/// Allows an entity to be set on fire, dealing damage-over-time.
/// Visual effects are handled by FireVisualComponent (auto-added when needed).
/// </summary>
public class FlammableComponent(
    float damageMultiplier = 1.0f,
    float baseDamagePerSecond = 5.0f,
    bool startOnFire = false
) : IActiveComponent
{
    private bool _startOnFire = startOnFire;
    // Config
    public float DamageMultiplier { get; set; } = damageMultiplier;
    public float BaseDamagePerSecond { get; set; } = baseDamagePerSecond;
    
    // State
    public bool IsBurning { get; private set; }
    
    // Events
    public event System.Action<FlammableComponent, bool> BurningStateChanged;

    // Cached component references
    private HealthComponent _health;
    private FireVisualComponent _fireVisuals;

    public Entity Entity { get; set; }

    public void SetOnFire()
    {
        if (IsBurning) return;
        IsBurning = true;

        // Ensure fire visuals exist and activate them
        if (_fireVisuals == null)
        {
            _fireVisuals = Entity.GetComponent<FireVisualComponent>();
            if (_fireVisuals == null)
            {
                // Auto-add fire visuals if not present
                _fireVisuals = new FireVisualComponent(startActive: true);
                Entity.AddComponent(_fireVisuals);
            }
        }
        
        _fireVisuals?.Activate();
        BurningStateChanged?.Invoke(this, true);
    }

    public void Extinguish()
    {
        if (!IsBurning) return;
        IsBurning = false;
        
        _fireVisuals?.Deactivate();
        BurningStateChanged?.Invoke(this, false);
    }

    public void OnStart()
    {
        if (_startOnFire)
        {
            SetOnFire();
        }
    }

    public void Update(double delta)
    {
        if (!IsBurning) return;

        // Apply damage-over-time if a health component exists
        if (_health != null && _health.IsAlive)
        {
            var dps = BaseDamagePerSecond * Mathf.Max(0f, DamageMultiplier);
            if (dps > 0f)
            {
                _health.TakeDamage(dps * (float)delta);
            }
        }
    }

    public void OnPreAttached() { }

    public void OnPostAttached()
    {
        _health = Entity.GetComponent<HealthComponent>();
        _fireVisuals = Entity.GetComponent<FireVisualComponent>();
        
        // If it's already burning when attached, ensure fire visuals are active
        if (IsBurning && _fireVisuals != null)
        {
            _fireVisuals.Activate();
        }
    }

    public IEnumerable<ComponentDependency> GetRequiredComponents()
    {
        yield return ComponentDependency.Of<TransformComponent2D>();
        // HealthComponent is optional; damage will be skipped if absent
        // FireVisualComponent is optional; will be auto-added when set on fire
    }

    public void OnDetached()
    {
        _health = null;
        _fireVisuals = null;
    }
}


