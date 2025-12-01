using Godot;
using Game.Data;
using Game.Data.Components;
using Game.Utils;
using System;
using System.Collections.Generic;

namespace Game.Data.Components;

/// <summary>
/// Visual representation component.
/// </summary>
public class VisualComponent(string ScenePath = null) : IComponent
{
	// Immediate-mode version using CustomEntityRenderEngine; legacy node fields kept for compatibility
	public Node2D ViewNode { get; private set; }
	public string ScenePath { get; set; } = ScenePath;
	public Node ParentNode { get; set; }

	public Vector2? ScaleMultiplier { get; set; }
	public Vector2 VisualOriginOffset { get; set; } = Vector2.Zero;
	public float? ZBiasOverride { get; set; }
	public bool YBasedZIndex { get; set; } = true;
	public float ZIndexScale { get; set; } = 1f;
	public int ZIndexOffset { get; set; } = 0;

	public Sprite2D Sprite { get; private set; }
	public string PendingSpritePath { get; set; }

	private TransformComponent2D _transform2D;
	private ulong _id;
	private bool _overlayDirty;
	private bool _hasOverlay;
	private float _overlayIntensity;
	private Color _overlayTop = Colors.Transparent;
	private Color _overlayBottom = Colors.Transparent;
	private Color _overlayBlend = Colors.White;
	private Vector2 _overlayDirection = Vector2.Up;

	public Entity Entity { get; set; }

	public void SetSprite(string path)
	{
		var texture = Resources.GetTexture(path);
		if (texture == null) return;
		if (_id != 0UL)
			EntityRendererFinder.Renderer?.UpdateSpriteTexture(_id, texture);
		// Maintain legacy Sprite node field for external code; but no node is created.
	}

	public void SetSprite(Texture2D texture)
	{
		if (texture == null) return;
		if (_id != 0UL)
			EntityRendererFinder.Renderer?.UpdateSpriteTexture(_id, texture);
	}

	private void PushTransform()
	{
		if (_id == 0UL || _transform2D == null) return;
		var scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One);
		var visualPosition = _transform2D.Position + VisualOriginOffset;
		EntityRendererFinder.Renderer?.UpdateSprite(_id, visualPosition, _transform2D.Rotation, scale);
		
		// Update ZBias if overridden (VisualOriginOffset no longer affects Z sorting automatically)
		float zBias = ZBiasOverride ?? 0f;
		EntityRendererFinder.Renderer?.UpdateSpriteZBias(_id, zBias);
		
		_transform2D.ClearDirty(TransformDirtyFlags.All);
	}

	public void OnPreAttached()
	{
		// No node creation; wait for transform and sprite path
	}

	public void OnPostAttached()
	{
		// Unsubscribe from any previous transform first (in case of component replacement)
		UnsubscribeFromTransform();

		var transform = Entity.GetComponent<TransformComponent2D>();
		if (transform == null) return;

		_transform2D = transform;

		// Subscribe to transform change events for event-driven updates
		_transform2D.PositionChanged += OnTransformChanged;
		_transform2D.RotationChanged += OnTransformChanged;
		_transform2D.ScaleChanged += OnTransformChanged;

		// Choose initial texture
		Texture2D texture = null;
		if (!string.IsNullOrEmpty(PendingSpritePath))
		{
			texture = Resources.GetTexture(PendingSpritePath);
		}

		var scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One);
		var visualPosition = _transform2D.Position + VisualOriginOffset;
		var zBias = ZBiasOverride ?? 0f;

		// Only add sprite if we have a valid texture
		if (texture != null)
		{
			_id = EntityRendererFinder.Renderer?.AddSprite(texture, visualPosition, _transform2D.Rotation, scale, null, zBias) ?? 0UL;
		}
		else
		{
			_id = 0UL; // No visual representation
		}
		_transform2D.ClearDirty(TransformDirtyFlags.All);

		// Clear pending
		PendingSpritePath = null;
		ApplyOverlay();
	}

	public IEnumerable<ComponentDependency> GetRequiredComponents()
	{
		yield return ComponentDependency.Of<TransformComponent2D>();
	}

	public void OnDetached()
	{
		if (_id != 0UL)
		{
			EntityRendererFinder.Renderer?.RemoveSprite(_id);
			_id = 0UL;
		}
		UnsubscribeFromTransform();
		_overlayDirty = false;
		_hasOverlay = false;
	}

	private void UnsubscribeFromTransform()
	{
		if (_transform2D != null)
		{
			_transform2D.PositionChanged -= OnTransformChanged;
			_transform2D.RotationChanged -= OnTransformChanged;
			_transform2D.ScaleChanged -= OnTransformChanged;
			_transform2D = null;
		}
	}

	public void SetGradientOverlay(float intensity, Color top, Color bottom, Color? blend = null, Vector2? direction = null)
	{
		_overlayIntensity = Mathf.Clamp(intensity, 0f, 1f);
		_overlayTop = top;
		_overlayBottom = bottom;
		_overlayBlend = blend ?? Colors.White;
		_overlayDirection = direction?.Normalized() ?? Vector2.Up;
		_hasOverlay = _overlayIntensity > 0f;
		_overlayDirty = true;
		ApplyOverlay();
	}

	public void ClearOverlay()
	{
		_hasOverlay = false;
		_overlayDirty = true;
		ApplyOverlay();
	}

	private void ApplyOverlay()
	{
		if (_id == 0UL || !_overlayDirty)
			return;

		var renderer = EntityRendererFinder.Renderer;
		if (renderer == null)
			return;

		if (_hasOverlay)
		{
			renderer.UpdateSpriteOverlayGradient(_id, _overlayIntensity, _overlayTop, _overlayBottom, _overlayBlend, _overlayDirection);
		}
		else
		{
			renderer.ClearSpriteOverlay(_id);
		}

		_overlayDirty = false;
	}

	private void OnTransformChanged(Entity entity)
	{
		// Verify the event is still from our current tracked transform
		// If the transform was replaced, ignore stale events
		if (_transform2D == null || entity != Entity) return;
		if (Entity.Transform != _transform2D) return;
		PushTransform();
	}
}
