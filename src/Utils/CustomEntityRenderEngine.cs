using System;
using System.Collections.Generic;
using Godot;

namespace Game.Utils;

/// <summary>
/// Immediate-mode 2D renderer that draws many sprites without per-entity nodes.
/// Sprites are globally sorted by world Y each frame (or when dirty) for painter's-order depth.
/// </summary>
public partial class CustomEntityRenderEngine : Node2D
{
	[Export] public bool UseShaderOverlay = true;
	private readonly Dictionary<Texture2D, Vector2> _textureSizeCache = new();

	private struct DebugArrow
	{
		public Vector2 From;
		public Vector2 To;
		public Color Color;
		public float Thickness;
	}

	private struct DebugLine
	{
		public Vector2 From;
		public Vector2 To;
		public Color Color;
		public float Thickness;
	}

	private struct DebugCircle
	{
		public Vector2 Center;
		public float Radius;
		public Color Color;
		public float Thickness;
		public int Segments;
	}

	private readonly struct SpriteKey : IEquatable<SpriteKey>
	{
		public readonly ulong Id;
		public SpriteKey(ulong id) { Id = id; }
		public bool Equals(SpriteKey other) => Id == other.Id;
		public override bool Equals(object obj) => obj is SpriteKey o && o.Id == Id;
		public override int GetHashCode() => Id.GetHashCode();
	}

	private sealed class Item
	{
		public ulong Id;
		public Texture2D Texture;
		public Vector2 Position;
		public float Rotation;
		public Vector2 Scale = Vector2.One;
		public Color Modulate = Colors.White;
		public float ZBias = 0f; // positive draws slightly above same-Y items
		public float OverlayIntensity = 0f;
		public Color OverlayTop = Colors.Transparent;
		public Color OverlayBottom = Colors.Transparent;
		public Color OverlayBlend = Colors.White;
		public Vector2 OverlayDirection = Vector2.Up;
	}

	private readonly List<Item> _items = new();
	private readonly Dictionary<SpriteKey, Item> _lookup = new();
	private ulong _nextId = 1;
	private bool _orderDirty = true;
	private readonly List<DebugArrow> _debugArrows = new();
	private readonly List<DebugLine> _debugLines = new();
	private readonly List<DebugCircle> _debugCircles = new();

	// Temporary arrays reused to avoid per-item allocations when drawing overlays
	private readonly Vector2[] _tmpVertices4 = new Vector2[4];
	private readonly Color[] _tmpColors4 = new Color[4];
	private static readonly Vector2[] _unitQuadUVs = new Vector2[4]
	{
		Vector2.Zero,
		new Vector2(1f, 0f),
		Vector2.One,
		new Vector2(0f, 1f)
	};

	[Export] public bool AlwaysRedraw = false; // event-driven redraws by default

	private ShaderMaterial _spriteMaterial;
	private Shader _spriteShader;

	// Cached parameter names to avoid per-call StringName allocations
	private static readonly StringName PARAM_INTENSITY = new StringName("overlay_intensity");
	private static readonly StringName PARAM_TOP = new StringName("overlay_top");
	private static readonly StringName PARAM_BOTTOM = new StringName("overlay_bottom");
	private static readonly StringName PARAM_BLEND = new StringName("overlay_blend");
	private static readonly StringName PARAM_DIR = new StringName("overlay_dir");

	// Track last-set values to avoid redundant SetShaderParameter calls
	private bool _hasLastShaderState = false;
	private float _lastIntensity;
	private Color _lastTop;
	private Color _lastBottom;
	private Color _lastBlend;
	private Vector2 _lastDir;

	private static bool ColorsApproximatelyEqual(Color a, Color b, float epsilon = 0.0005f)
	{
		return Mathf.IsEqualApprox(a.R, b.R, epsilon)
			&& Mathf.IsEqualApprox(a.G, b.G, epsilon)
			&& Mathf.IsEqualApprox(a.B, b.B, epsilon)
			&& Mathf.IsEqualApprox(a.A, b.A, epsilon);
	}



	public override void _Ready()
	{
		base._Ready();
		CustomEntityRenderEngineLocator.Renderer = this;
		SetProcess(true);

	if (UseShaderOverlay)
		{
			_spriteShader = new Shader();
			_spriteShader.Code = "shader_type canvas_item;\n\nuniform float overlay_intensity = 0.0;\nuniform vec4 overlay_top = vec4(0.0);\nuniform vec4 overlay_bottom = vec4(0.0);\nuniform vec4 overlay_blend = vec4(1.0);\nuniform vec2 overlay_dir = vec2(0.0, 1.0);\n\nvoid fragment() {\n    vec4 modulate_col = COLOR;\n    vec4 base_tex = texture(TEXTURE, UV);\n    vec4 base_col = base_tex * modulate_col;\n\n    if (overlay_intensity <= 0.0) {\n        COLOR = base_col;\n    } else {\n        vec2 axis = overlay_dir;\n        float len = length(axis);\n        if (len < 1e-4) axis = vec2(0.0, 1.0); else axis = axis / len;\n        float t = clamp(dot(UV, axis), 0.0, 1.0);\n\n        vec4 top = overlay_top;\n        vec4 bottom = overlay_bottom;\n        top.a = clamp(top.a * overlay_intensity, 0.0, 1.0);\n        bottom.a = clamp(bottom.a * overlay_intensity, 0.0, 1.0);\n        if (overlay_intensity > 0.0) {\n            float blendAmount = overlay_intensity * 0.5;\n            top = mix(top, overlay_blend, blendAmount);\n            bottom = mix(bottom, overlay_blend, blendAmount);\n        }\n        vec4 grad = mix(bottom, top, t);\n\n        // Alpha compositing: grad over base\n        vec4 out_col = base_col * (1.0 - grad.a) + vec4(grad.rgb, 1.0) * grad.a;\n        COLOR = out_col;\n    }\n}";
			_spriteMaterial = new ShaderMaterial { Shader = _spriteShader };
			Material = _spriteMaterial;
		}
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (CustomEntityRenderEngineLocator.Renderer == this)
			CustomEntityRenderEngineLocator.Renderer = null;
	}

	public override void _Process(double delta)
	{
		if (AlwaysRedraw)
			QueueRedraw();
	}

	public void QueueDebugArrow(Vector2 from, Vector2 to, Color color, float thickness = 2f)
	{
		_debugArrows.Add(new DebugArrow { From = from, To = to, Color = color, Thickness = thickness });
		QueueRedraw();
	}

	public void QueueDebugVector(Vector2 origin, Vector2 vector, Color color, float thickness = 2f)
	{
		QueueDebugArrow(origin, origin + vector, color, thickness);
	}

	public void QueueDebugLine(Vector2 from, Vector2 to, Color color, float thickness = 2f)
	{
		_debugLines.Add(new DebugLine { From = from, To = to, Color = color, Thickness = thickness });
		QueueRedraw();
	}

	public void QueueDebugCircle(Vector2 center, float radius, Color color, float thickness = 2f, int segments = 24)
	{
		if (segments < 4) segments = 4;
		if (radius < 0f) radius = 0f;
		_debugCircles.Add(new DebugCircle { Center = center, Radius = radius, Color = color, Thickness = thickness, Segments = segments });
		QueueRedraw();
	}

	public ulong AddSprite(Texture2D texture, Vector2 position, float rotation = 0f, Vector2? scale = null, Color? modulate = null, float zBias = 0f)
	{
		if (texture == null) throw new ArgumentNullException(nameof(texture));
		var id = _nextId++;
		var item = new Item
		{
			Id = id,
			Texture = texture,
			Position = position,
			Rotation = rotation,
			Scale = scale ?? Vector2.One,
			Modulate = modulate ?? Colors.White,
			ZBias = zBias
		};
		_items.Add(item);
		_lookup[new SpriteKey(id)] = item;
		_orderDirty = true;
		QueueRedraw();
		return id;
	}

	public void UpdateSprite(ulong id, Vector2 position, float rotation, Vector2 scale)
	{
		if (!_lookup.TryGetValue(new SpriteKey(id), out var item)) return;
		if (!Mathf.IsEqualApprox(item.Position.Y, position.Y))
			_orderDirty = true;
		item.Position = position;
		item.Rotation = rotation;
		item.Scale = scale;
		QueueRedraw();
	}

	public void UpdateSpriteTexture(ulong id, Texture2D texture)
	{
		if (texture == null) return;
		if (_lookup.TryGetValue(new SpriteKey(id), out var item))
			item.Texture = texture;
		QueueRedraw();
	}

	public void UpdateSpriteModulate(ulong id, Color color)
	{
		if (_lookup.TryGetValue(new SpriteKey(id), out var item))
			item.Modulate = color;
		QueueRedraw();
	}

	public void UpdateSpriteZBias(ulong id, float zBias)
	{
		if (_lookup.TryGetValue(new SpriteKey(id), out var item))
		{
			if (!Mathf.IsEqualApprox(item.ZBias, zBias))
			{
				item.ZBias = zBias;
				_orderDirty = true;
				QueueRedraw();
			}
		}
	}

	public void RemoveSprite(ulong id)
	{
		var key = new SpriteKey(id);
		if (!_lookup.TryGetValue(key, out var item)) return;
		_lookup.Remove(key);
		int idx = _items.IndexOf(item);
		if (idx >= 0)
		{
			int last = _items.Count - 1;
			_items[idx] = _items[last];
			_items.RemoveAt(last);
		}
		_orderDirty = true;
		QueueRedraw();
	}

	public void UpdateSpriteOverlayGradient(ulong id, float intensity, Color topColor, Color bottomColor, Color? blendColor = null, Vector2? direction = null)
	{
		if (!_lookup.TryGetValue(new SpriteKey(id), out var item)) return;
		item.OverlayIntensity = Mathf.Clamp(intensity, 0f, 1f);
		item.OverlayTop = topColor;
		item.OverlayBottom = bottomColor;
		if (blendColor.HasValue)
			item.OverlayBlend = blendColor.Value;
		if (direction.HasValue && direction.Value != Vector2.Zero)
			item.OverlayDirection = direction.Value.Normalized();
		QueueRedraw();
	}

	public void ClearSpriteOverlay(ulong id)
	{
		if (!_lookup.TryGetValue(new SpriteKey(id), out var item)) return;
		item.OverlayIntensity = 0f;
		item.OverlayTop = Colors.Transparent;
		item.OverlayBottom = Colors.Transparent;
		item.OverlayBlend = Colors.White;
		item.OverlayDirection = Vector2.Up;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_orderDirty)
		{
			_items.Sort((a, b) =>
			{
				var ay = a.Position.Y + a.ZBias;
				var by = b.Position.Y + b.ZBias;
				return ay.CompareTo(by);
			});
			_orderDirty = false;
		}

		for (int i = 0; i < _items.Count; i++)
		{
			var it = _items[i];
			if (it.Texture == null) continue;

			DrawSetTransform(it.Position, it.Rotation, it.Scale);
			if (!_textureSizeCache.TryGetValue(it.Texture, out var size))
			{
				size = it.Texture.GetSize();
				_textureSizeCache[it.Texture] = size;
			}
			// var size = it.Texture.GetSize();
			var rect = new Rect2(-size * 0.5f, size);
			if (UseShaderOverlay && _spriteMaterial != null)
			{
				var uvDir = it.OverlayDirection;
				if (uvDir == Vector2.Zero) uvDir = Vector2.Up;
				uvDir = uvDir.Rotated(-it.Rotation).Normalized();

				// Only set uniforms when changed to reduce interop overhead
				if (!_hasLastShaderState || !Mathf.IsEqualApprox(_lastIntensity, it.OverlayIntensity))
				{
					_spriteMaterial.SetShaderParameter(PARAM_INTENSITY, it.OverlayIntensity);
					_lastIntensity = it.OverlayIntensity;
				}
				if (!_hasLastShaderState || !ColorsApproximatelyEqual(_lastTop, it.OverlayTop))
				{
					_spriteMaterial.SetShaderParameter(PARAM_TOP, it.OverlayTop);
					_lastTop = it.OverlayTop;
				}
				if (!_hasLastShaderState || !ColorsApproximatelyEqual(_lastBottom, it.OverlayBottom))
				{
					_spriteMaterial.SetShaderParameter(PARAM_BOTTOM, it.OverlayBottom);
					_lastBottom = it.OverlayBottom;
				}
				if (!_hasLastShaderState || !ColorsApproximatelyEqual(_lastBlend, it.OverlayBlend))
				{
					_spriteMaterial.SetShaderParameter(PARAM_BLEND, it.OverlayBlend);
					_lastBlend = it.OverlayBlend;
				}
				if (!_hasLastShaderState || !_lastDir.IsEqualApprox(uvDir))
				{
					_spriteMaterial.SetShaderParameter(PARAM_DIR, uvDir);
					_lastDir = uvDir;
				}
				_hasLastShaderState = true;
				DrawTextureRect(it.Texture, rect, false, it.Modulate);
			}
			else
			{
				DrawTextureRect(it.Texture, rect, false, it.Modulate);
				if (it.OverlayIntensity > 0f)
				{
					DrawGradientOverlay(rect, it);
				}
			}
		}

		// Reset transform so debug overlays draw in world space
		DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

		// Draw debug primitives last so they appear over sprites
		if (UseShaderOverlay && _spriteMaterial != null)
		{
			if (!_hasLastShaderState || !Mathf.IsEqualApprox(_lastIntensity, 0.0f))
			{
				_spriteMaterial.SetShaderParameter(PARAM_INTENSITY, 0.0f);
				_lastIntensity = 0.0f;
			}
			_hasLastShaderState = true;
		}

		// Reset cached state so next frame compares fresh
		_hasLastShaderState = false;
		for (int i = 0; i < _debugArrows.Count; i++)
		{
			var a = _debugArrows[i];
			DrawLine(a.From, a.To, a.Color, a.Thickness);

			var dir = (a.To - a.From);
			var len = dir.Length();
			if (len > 0.0001f)
			{
				var nd = dir / len;
				float headSize = Mathf.Min(12f, len * 0.25f);
				var right = new Vector2(-nd.Y, nd.X);
				var tip = a.To;
				var basePoint = a.To - nd * headSize;
				var leftWing = basePoint + right * (headSize * 0.4f);
				var rightWing = basePoint - right * (headSize * 0.4f);
				DrawLine(tip, leftWing, a.Color, a.Thickness);
				DrawLine(tip, rightWing, a.Color, a.Thickness);
			}
		}

		for (int i = 0; i < _debugLines.Count; i++)
		{
			var l = _debugLines[i];
			DrawLine(l.From, l.To, l.Color, l.Thickness);
		}

		for (int i = 0; i < _debugCircles.Count; i++)
		{
			var c = _debugCircles[i];
			DrawArc(c.Center, c.Radius, 0f, Mathf.Tau, c.Segments, c.Color, c.Thickness);
		}

		_debugArrows.Clear();
		_debugLines.Clear();
		_debugCircles.Clear();
	}

	private void DrawGradientOverlay(Rect2 rect, Item item)
	{
		// Skip completely if no visible overlay
		if (item.OverlayIntensity <= 0f) return;

		var axis = item.OverlayDirection;
		if (axis == Vector2.Zero)
			axis = Vector2.Up;
		axis = axis.Normalized();

		var vertices = _tmpVertices4;
		vertices[0] = rect.Position;
		vertices[1] = rect.Position + new Vector2(rect.Size.X, 0f);
		vertices[2] = rect.Position + rect.Size;
		vertices[3] = rect.Position + new Vector2(0f, rect.Size.Y);

		float min = float.MaxValue;
		float max = float.MinValue;
		for (int i = 0; i < vertices.Length; i++)
		{
			float dot = vertices[i].Dot(axis);
			if (dot < min) min = dot;
			if (dot > max) max = dot;
		}

		float range = Mathf.Max(0.0001f, max - min);
		var top = item.OverlayTop;
		var bottom = item.OverlayBottom;

		// Scale alpha by intensity and blend toward overlay blend color.
		top.A = Mathf.Clamp(top.A * item.OverlayIntensity, 0f, 1f);
		bottom.A = Mathf.Clamp(bottom.A * item.OverlayIntensity, 0f, 1f);

		if (item.OverlayIntensity > 0f)
		{
			float blendAmount = item.OverlayIntensity * 0.5f;
			top = top.Lerp(item.OverlayBlend, blendAmount);
			bottom = bottom.Lerp(item.OverlayBlend, blendAmount);
		}

		var colors = _tmpColors4;
		for (int i = 0; i < vertices.Length; i++)
		{
			float t = Mathf.Clamp((vertices[i].Dot(axis) - min) / range, 0f, 1f);
			colors[i] = bottom.Lerp(top, t);
		}

		DrawPolygon(vertices, colors, _unitQuadUVs, item.Texture);
	}
}

public static class CustomEntityRenderEngineLocator
{
	public static CustomEntityRenderEngine Renderer { get; set; }
}
