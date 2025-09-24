using Godot;
using Game.Data;
using Game.Utils;
using System.Collections.Generic;

namespace Game.Data.Components;

/// <summary>
/// Allows an entity to be set on fire, dealing damage-over-time and rendering a flame overlay.
/// Rendering uses the immediate-mode CustomEntityRenderEngine with a small Z-bias so flames
/// draw above the base sprite while still respecting Y-sorted depth overall.
/// </summary>
public class FlammableComponent(
    float damageMultiplier = 1.0f,
    float baseDamagePerSecond = 5.0f,
    string flameTexturePath = null,
    bool startOnFire = false
) : IActiveComponent
{
    private bool _startOnFire = startOnFire;
    // Config
    public float DamageMultiplier { get; set; } = damageMultiplier;
    public float BaseDamagePerSecond { get; set; } = baseDamagePerSecond;
    public string FlameTexturePath { get; set; } = flameTexturePath;
    // State
    public bool IsBurning { get; private set; }
    
    // Events
    public event System.Action<FlammableComponent, bool> BurningStateChanged;

    // Cached component references
    private TransformComponent2D _transform2D;
    private HealthComponent _health;

    // Render state
    private ulong _flameSpriteId;
    private Texture2D _flameTexture;
    private float _time;
    private VisualComponent _visual;

    // Tunables for visuals
    private const float FlameZBias = 0.1f; // small positive bias to render on top of base sprite
    private static readonly Color FlameTint = new Color(1.0f, 0.6f, 0.2f, 0.9f);
    private static readonly Color OverlayTopColor = new Color(1.0f, 0.45f, 0.05f, 0.85f);
    private static readonly Color OverlayBottomColor = new Color(1.0f, 0.9f, 0.35f, 0.55f);
    private static readonly Color OverlayBlendColor = new Color(1.0f, 0.85f, 0.6f, 1.0f);

    public Entity Entity { get; set; }

    public void SetOnFire()
    {
        if (IsBurning) return;
        IsBurning = true;

        TryCreateFlameOverlay();
        ApplyOverlay(true);
        BurningStateChanged?.Invoke(this, true);
    }

    public void Extinguish()
    {
        if (!IsBurning) return;
        IsBurning = false;
        RemoveFlameOverlay();
        ApplyOverlay(false);
        BurningStateChanged?.Invoke(this, false);
    }

    public void OnStart(){
        if (_startOnFire){
            SetOnFire();
        }
    }

    public void Update(double delta)
    {
        if (!IsBurning) return;

        _time += (float)delta;

        // 1) Apply damage-over-time if a health component exists
        if (_health != null && _health.IsAlive)
        {
            var dps = BaseDamagePerSecond * Mathf.Max(0f, DamageMultiplier);
            if (dps > 0f)
            {
                _health.TakeDamage(dps * (float)delta);
            }
        }

        // 2) Update overlay transform and a subtle flicker
        if (_flameSpriteId != 0UL && _transform2D != null && CustomEntityRenderEngineLocator.Renderer != null)
        {
            // Flicker scale between 0.9 and 1.1
            var flicker = 1.0f + 0.1f * Mathf.Sin(_time * 18.0f);
            var scale = _transform2D.Scale * flicker;
            CustomEntityRenderEngineLocator.Renderer.UpdateSprite(_flameSpriteId, _transform2D.Position, _transform2D.Rotation, scale);

            // Slight alpha pulse for warmth
            var alpha = 0.75f + 0.2f * Mathf.Sin(_time * 12.0f + 1.57f);
            var tinted = new Color(FlameTint.R, FlameTint.G, FlameTint.B, alpha);
            CustomEntityRenderEngineLocator.Renderer.UpdateSpriteModulate(_flameSpriteId, tinted);
        }
        else if (_transform2D != null && CustomEntityRenderEngineLocator.Renderer != null)
        {
            // Fallback: draw a simple debug circle ring if no texture overlay is available
            // This ensures some visible feedback even without assets.
            var r = 8f + 2f * Mathf.Sin(_time * 15.0f);
            CustomEntityRenderEngineLocator.Renderer.QueueDebugCircle(_transform2D.Position, r, new Color(1f, 0.4f, 0.1f, 0.85f), 2f, 24);
        }

        UpdateOverlayPulse();
    }

    public void OnPreAttached() { }

    public void OnPostAttached()
    {
        _transform2D = Entity.GetComponent<TransformComponent2D>();
        _health = Entity.GetComponent<HealthComponent>();
        _visual = Entity.GetComponent<VisualComponent>();

        // If it's already burning when attached, ensure overlay exists
        if (IsBurning)
        {
            TryCreateFlameOverlay();
            ApplyOverlay(true);
        }
    }

    public IEnumerable<ComponentDependency> GetRequiredComponents()
    {
        yield return ComponentDependency.Of<TransformComponent2D>();
        // HealthComponent is optional; damage will be skipped if absent
    }

    public void OnDetached()
    {
        RemoveFlameOverlay();
        _transform2D = null;
        _health = null;
        _visual = null;
    }

    private void TryCreateFlameOverlay()
    {
        if (_flameSpriteId != 0UL) return;
        if (_transform2D == null || CustomEntityRenderEngineLocator.Renderer == null) return;

        // Load flame texture if path provided
        if (!string.IsNullOrEmpty(FlameTexturePath))
        {
            _flameTexture = Resources.GetTexture(FlameTexturePath);
        }

        if (_flameTexture != null)
        {
            _flameSpriteId = CustomEntityRenderEngineLocator.Renderer.AddSprite(
                _flameTexture,
                _transform2D.Position,
                _transform2D.Rotation,
                _transform2D.Scale,
                FlameTint,
                FlameZBias
            );
        }
        // else: rely on debug circle fallback in Update
    }

    private void RemoveFlameOverlay()
    {
        if (_flameSpriteId != 0UL && CustomEntityRenderEngineLocator.Renderer != null)
        {
            CustomEntityRenderEngineLocator.Renderer.RemoveSprite(_flameSpriteId);
        }
        _flameSpriteId = 0UL;
        _flameTexture = null;
    }

    private void ApplyOverlay(bool enable)
    {
        if (_visual == null) return;

        if (enable)
        {
            var baseIntensity = 0.65f;
            _visual.SetGradientOverlay(baseIntensity, OverlayTopColor, OverlayBottomColor, OverlayBlendColor, Vector2.Up);
        }
        else
        {
            _visual.ClearOverlay();
        }
    }

    private void UpdateOverlayPulse()
    {
        if (_visual == null) return;
        var pulse = 0.55f + 0.25f * Mathf.Sin(_time * 6.5f);
        var top = OverlayTopColor;
        var bottom = OverlayBottomColor;
        var blend = OverlayBlendColor;

        // Slight flicker on alpha for top/bottom
        float flicker = 0.1f * Mathf.Sin(_time * 9.0f + 0.75f);
        top.A = Mathf.Clamp(top.A + flicker, 0f, 1f);
        bottom.A = Mathf.Clamp(bottom.A + flicker * 0.5f, 0f, 1f);

        _visual.SetGradientOverlay(pulse, top, bottom, blend, Vector2.Up);
    }
}


