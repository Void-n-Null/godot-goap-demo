using Godot;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Visual component backed by CustomEntityRenderEngine (immediate mode). No per-entity nodes.
/// </summary>
public class YSortedVisualComponent(Texture2D Texture, Vector2? ScaleMultiplier = null) : IActiveComponent
{
    public Texture2D Texture { get; set; } = Texture;
    public Vector2? ScaleMultiplier { get; set; } = ScaleMultiplier;

    private TransformComponent2D _transform2D;
    private ulong _id;

    public Entity Entity { get; set; }

    public void Update(double delta)
    {
        if (_id == 0UL || _transform2D == null) return;
        var dirty = _transform2D.DirtyMask;
        if (dirty == TransformDirtyFlags.None) return;

        var scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One);
        CustomEntityRenderEngineLocator.Renderer?.UpdateSprite(_id, _transform2D.Position, _transform2D.Rotation, scale);
        _transform2D.ClearDirty(dirty);
    }

    public void OnPreAttached() { }

    public void OnPostAttached()
    {
        _transform2D = Entity.GetComponent<TransformComponent2D>();
        if (_transform2D == null || Texture == null) return;
        var scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One);
        _id = CustomEntityRenderEngineLocator.Renderer?.AddSprite(Texture, _transform2D.Position, _transform2D.Rotation, scale) ?? 0UL;
        _transform2D.ClearDirty(TransformDirtyFlags.All);
    }

    public void OnDetached()
    {
        if (_id != 0UL)
        {
            CustomEntityRenderEngineLocator.Renderer?.RemoveSprite(_id);
            _id = 0UL;
        }
        _transform2D = null;
    }
}


