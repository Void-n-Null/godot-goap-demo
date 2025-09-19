using Godot;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Test component that randomly switches the texture of a BatchedVisualComponent
/// among a fixed set, at random intervals. Proves batch reallocation works.
/// </summary>
public class SwitchSkinTest(string[] TexturePaths = null) : IActiveComponent
{
    public string[] TexturePaths { get; set; } = TexturePaths ?? new []
    {
        "res://textures/Boy.png",
        "res://textures/Female.png",
        "res://textures/Girl.png",
        "res://textures/Male.png",
    };

    private BatchedVisualComponent _batched;
    private TransformComponent2D _transform;
    private float _timeLeft;

    public Entity Entity { get; set; }

    public void OnPreAttached() { }

    public void OnPostAttached()
    {
        _batched = Entity.GetComponent<BatchedVisualComponent>();
        _transform = Entity.GetComponent<TransformComponent2D>();
        _timeLeft = Random.NextFloat(0.25f, 1.25f);
    }

    public void Update(double delta)
    {
        if (_batched == null || _transform == null) return;
        _timeLeft -= (float)delta;
        if (_timeLeft <= 0f)
        {
            // Pick a new texture
            var path = Random.NextItem(TexturePaths);
            var tex = Resources.GetTexture(path);
            if (tex != null)
            {
                _batched.Texture = tex;
                // Recompute transform for the new texture size
                var texSize = tex.GetSize();
                var scale = _transform.Scale * (_batched.ScaleMultiplier ?? Vector2.One) * new Vector2(texSize.X, texSize.Y);
                scale.Y = -scale.Y;
                var xform = new Transform2D(_transform.Rotation, Vector2.Zero).Scaled(scale);
                xform.Origin = _transform.Position;
                // Relocate the instance to the new texture batch
                BatchRendererLocator.Renderer?.RelocateInstanceTexture(_batched.InstanceId, tex, xform);
                _transform.ClearDirty(TransformDirtyFlags.All);
            }
            _timeLeft = Random.NextFloat(0.25f, 1.25f);
        }
    }

    public void OnDetached() { }

    
}


