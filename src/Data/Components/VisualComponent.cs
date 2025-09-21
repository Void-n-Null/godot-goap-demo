using Godot;
using Game.Data;
using Game.Data.Components;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Visual representation component.
/// </summary>
public class VisualComponent(string ScenePath = null) : IActiveComponent
{
	// Immediate-mode version using CustomEntityRenderEngine; legacy node fields kept for compatibility
	public Node2D ViewNode { get; private set; }
	public string ScenePath { get; set; } = ScenePath;
	public Node ParentNode { get; set; }

	public Vector2? ScaleMultiplier { get; set; }
	public bool YBasedZIndex { get; set; } = true;
	public float ZIndexScale { get; set; } = 1f;
	public int ZIndexOffset { get; set; } = 0;

	public Sprite2D Sprite { get; private set; }
	public string PendingSpritePath { get; set; }

	private TransformComponent2D _transform2D;
	private ulong _id;

	public Entity Entity { get; set; }

	public void SetSprite(string path)
	{
		var texture = Resources.GetTexture(path);
		if (texture == null) return;
		if (_id != 0UL)
			CustomEntityRenderEngineLocator.Renderer?.UpdateSpriteTexture(_id, texture);
		// Maintain legacy Sprite node field for external code; but no node is created.
	}

	public void SetSprite(Texture2D texture)
	{
		if (texture == null) return;
		if (_id != 0UL)
			CustomEntityRenderEngineLocator.Renderer?.UpdateSpriteTexture(_id, texture);
	}

	public void Update(double delta)
	{
		if (_id == 0UL || _transform2D == null) return;
		var dirty = _transform2D.DirtyMask;
		if (dirty == TransformDirtyFlags.None) return;
		var scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One);
		CustomEntityRenderEngineLocator.Renderer?.UpdateSprite(_id, _transform2D.Position, _transform2D.Rotation, scale);
		_transform2D.ClearDirty(dirty);
	}

	public void OnPreAttached()
	{
		// No node creation; wait for transform and sprite path
	}

	public void OnPostAttached()
	{
		_transform2D = Entity.GetComponent<TransformComponent2D>();
		if (_transform2D == null) return;

		// Choose initial texture
		Texture2D texture = null;
		if (!string.IsNullOrEmpty(PendingSpritePath))
			texture = Resources.GetTexture(PendingSpritePath);

		var scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One);
		_id = CustomEntityRenderEngineLocator.Renderer?.AddSprite(texture, _transform2D.Position, _transform2D.Rotation, scale) ?? 0UL;
		_transform2D.ClearDirty(TransformDirtyFlags.All);

		// Clear pending
		PendingSpritePath = null;
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
