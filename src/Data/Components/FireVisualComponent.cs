using Godot;
using Game.Data;
using Game.Utils;
using System.Collections.Generic;

namespace Game.Data.Components;

/// <summary>
/// Renders fire visual effects (flame sprite overlay, color pulsing, flickering).
/// Used for both entities that ARE fire (campfires) and entities ON fire (burning objects).
/// </summary>
public class FireVisualComponent(
    string flameTexturePath = "res://textures/Flame.png",
    float intensity = 1.0f,
    bool startActive = true,
    Vector2? scaleMultiplier = null,
    Vector2? visualOffset = null
) : IActiveComponent
{
    // Config
    public string FlameTexturePath { get; set; } = flameTexturePath;
    public float Intensity { get; set; } = intensity;
    public Vector2 ScaleMultiplier { get; set; } = scaleMultiplier ?? Vector2.One;
    public bool IsActive { get; private set; } = startActive;
    public Vector2 VisualOffset { get; set; } = visualOffset ?? Vector2.Zero;

    // Cached component references
    private TransformComponent2D _transform2D;
    private VisualComponent _visual;

    // Render state
    private ulong _flameSpriteId;
    private Texture2D _flameTexture;
    private float _time;

    // Visual tunables
    private const float FlameZBias = 0.1f; // Render on top of base sprite
    private static readonly Color FlameTint = new Color(1.0f, 0.6f, 0.2f, 0.9f);
    private static readonly Color OverlayTopColor = new Color(1.0f, 0.45f, 0.05f, 0.85f);
    private static readonly Color OverlayBottomColor = new Color(1.0f, 0.9f, 0.35f, 0.55f);
    private static readonly Color OverlayBlendColor = new Color(1.0f, 0.85f, 0.6f, 1.0f);

    public Entity Entity { get; set; }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        TryCreateFlameOverlay();
        ApplyOverlay(true);
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        RemoveFlameOverlay();
        ApplyOverlay(false);
    }

    public void OnStart()
    {
        if (IsActive)
        {
            TryCreateFlameOverlay();
            ApplyOverlay(true);
        }
    }

    public void Update(double delta)
    {
        if (!IsActive) return;

        _time += (float)delta;

        // Update flame sprite transform with flicker
        if (_flameSpriteId != 0UL && _transform2D != null && EntityRendererFinder.Renderer != null)
        {
            // Flicker scale between 0.9 and 1.1
            var flicker = 1.0f + 0.1f * Mathf.Sin(_time * 18.0f);
            var scale = _transform2D.Scale * (flicker * Intensity) * ScaleMultiplier;
            var position = _transform2D.Position + VisualOffset;
            EntityRendererFinder.Renderer.UpdateSprite(_flameSpriteId, position, _transform2D.Rotation, scale);

            // Alpha pulse for warmth
            var alpha = 0.75f + 0.2f * Mathf.Sin(_time * 12.0f + 1.57f);
            var tinted = new Color(FlameTint.R, FlameTint.G, FlameTint.B, alpha * Intensity);
            EntityRendererFinder.Renderer.UpdateSpriteModulate(_flameSpriteId, tinted);
        }
        else if (_transform2D != null && EntityRendererFinder.Renderer != null)
        {
            // Fallback: debug circle if no texture available
            var r = 8f + 2f * Mathf.Sin(_time * 15.0f);
            var position = _transform2D.Position + VisualOffset;
            // Use X scale for primary size
            EntityRendererFinder.Renderer.QueueDebugCircle(
                position,
                r * Intensity * ScaleMultiplier.X,
                new Color(1f, 0.4f, 0.1f, 0.85f * Intensity),
                2f,
                24
            );
        }

        UpdateOverlayPulse();
    }

    public void OnPreAttached() { }

    public void OnPostAttached()
    {
        _transform2D = Entity.GetComponent<TransformComponent2D>();
        _visual = Entity.GetComponent<VisualComponent>();

        if (IsActive)
        {
            TryCreateFlameOverlay();
            ApplyOverlay(true);
        }
    }

    public IEnumerable<ComponentDependency> GetRequiredComponents()
    {
        yield return ComponentDependency.Of<TransformComponent2D>();
    }

    public void OnDetached()
    {
        RemoveFlameOverlay();
        _transform2D = null;
        _visual = null;
    }

    private void TryCreateFlameOverlay()
    {
        if (_flameSpriteId != 0UL) return;
        if (_transform2D == null || EntityRendererFinder.Renderer == null) return;

        // Load flame texture
        if (!string.IsNullOrEmpty(FlameTexturePath))
        {
            _flameTexture = Resources.GetTexture(FlameTexturePath);
        }

        if (_flameTexture != null)
        {
            var position = _transform2D.Position + VisualOffset;
            _flameSpriteId = EntityRendererFinder.Renderer.AddSprite(
                _flameTexture,
                position,
                _transform2D.Rotation,
                _transform2D.Scale * Intensity * ScaleMultiplier,
                FlameTint,
                FlameZBias
            );
        }
    }

    private void RemoveFlameOverlay()
    {
        if (_flameSpriteId != 0UL && EntityRendererFinder.Renderer != null)
        {
            EntityRendererFinder.Renderer.RemoveSprite(_flameSpriteId);
        }
        _flameSpriteId = 0UL;
        _flameTexture = null;
    }

    private void ApplyOverlay(bool enable)
    {
        if (_visual == null) return;

        if (enable)
        {
            var baseIntensity = 0.65f * Intensity;
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
        
        var pulse = (0.55f + 0.25f * Mathf.Sin(_time * 6.5f)) * Intensity;
        var top = OverlayTopColor;
        var bottom = OverlayBottomColor;
        var blend = OverlayBlendColor;

        // Slight flicker on alpha
        float flicker = 0.1f * Mathf.Sin(_time * 9.0f + 0.75f);
        top.A = Mathf.Clamp((top.A + flicker) * Intensity, 0f, 1f);
        bottom.A = Mathf.Clamp((bottom.A + flicker * 0.5f) * Intensity, 0f, 1f);

        _visual.SetGradientOverlay(pulse, top, bottom, blend, Vector2.Up);
    }
}


