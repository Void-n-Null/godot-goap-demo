using Godot;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Visual component that renders via BatchRenderer2D using a shared texture batch instead of per-entity nodes.
/// </summary>
public class BatchedVisualComponent(Texture2D Texture, string ScenePath = null) : IActiveComponent
{
    public Texture2D Texture { get; set; } = Texture;
    public string ScenePath { get; set; } = ScenePath; // unused here; for parity with VisualComponent
    public Vector2? ScaleMultiplier { get; set; }

    private TransformComponent2D _transform2D;
    private ulong _instanceId;

    public Entity Entity { get; set; }

    public void Update(double delta)
    {
        if (_transform2D == null || Texture == null) return;
        var dirty = _transform2D.DirtyMask;
        if (dirty == TransformDirtyFlags.None) return;

        var texSize = Texture.GetSize();
        var scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One) * new Vector2(texSize.X, texSize.Y);
        // Flip Y to match Godot 2D coordinate system with QuadMesh UV orientation
        scale.Y = -scale.Y;
        var xform = new Transform2D(_transform2D.Rotation, Vector2.Zero);
        xform = xform.Scaled(scale);
        xform.Origin = _transform2D.Position;
        BatchRendererLocator.Renderer?.UpdateInstance(_instanceId, xform);
        _transform2D.ClearDirty(dirty);
    }

    public void OnPreAttached()
    {
        // Nothing to do here; Batch registration happens in PostAttached when we have Transform
    }

    public void OnPostAttached()
    {
        _transform2D = Entity.GetComponent<TransformComponent2D>();
        if (_transform2D == null || Texture == null) return;

        var texSize = Texture.GetSize();
        var scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One) * new Vector2(texSize.X, texSize.Y);
        // Flip Y to match Godot 2D coordinate system with QuadMesh UV orientation
        scale.Y = -scale.Y;
        var xform = new Transform2D(_transform2D.Rotation, Vector2.Zero).Scaled(scale);
        xform.Origin = _transform2D.Position;
        _instanceId = BatchRendererLocator.Renderer?.AddInstance(Texture, xform) ?? 0UL;
        _transform2D.ClearDirty(TransformDirtyFlags.All);
    }

    public void OnDetached()
    {
        if (_instanceId != 0UL)
        {
            BatchRendererLocator.Renderer?.RemoveInstance(_instanceId);
            _instanceId = 0UL;
        }
        _transform2D = null;
    }
}

public static class BatchRendererLocator
{
    public static BatchRenderer2D Renderer { get; set; }
}


