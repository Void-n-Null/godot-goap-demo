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

    [Export] public bool AlwaysRedraw = true; // set false if you want event-driven redraws

    public override void _Ready()
    {
        base._Ready();
        CustomEntityRenderEngineLocator.Renderer = this;
        SetProcess(true);
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
    }

    public void QueueDebugVector(Vector2 origin, Vector2 vector, Color color, float thickness = 2f)
    {
        QueueDebugArrow(origin, origin + vector, color, thickness);
    }

    public void QueueDebugLine(Vector2 from, Vector2 to, Color color, float thickness = 2f)
    {
        _debugLines.Add(new DebugLine { From = from, To = to, Color = color, Thickness = thickness });
    }

    public void QueueDebugCircle(Vector2 center, float radius, Color color, float thickness = 2f, int segments = 24)
    {
        if (segments < 4) segments = 4;
        if (radius < 0f) radius = 0f;
        _debugCircles.Add(new DebugCircle { Center = center, Radius = radius, Color = color, Thickness = thickness, Segments = segments });
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
    }

    public void UpdateSpriteTexture(ulong id, Texture2D texture)
    {
        if (texture == null) return;
        if (_lookup.TryGetValue(new SpriteKey(id), out var item))
            item.Texture = texture;
    }

    public void UpdateSpriteModulate(ulong id, Color color)
    {
        if (_lookup.TryGetValue(new SpriteKey(id), out var item))
            item.Modulate = color;
    }

    public void UpdateSpriteZBias(ulong id, float zBias)
    {
        if (_lookup.TryGetValue(new SpriteKey(id), out var item))
        {
            if (!Mathf.IsEqualApprox(item.ZBias, zBias))
            {
                item.ZBias = zBias;
                _orderDirty = true;
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
    }

    public void ClearSpriteOverlay(ulong id)
    {
        if (!_lookup.TryGetValue(new SpriteKey(id), out var item)) return;
        item.OverlayIntensity = 0f;
        item.OverlayTop = Colors.Transparent;
        item.OverlayBottom = Colors.Transparent;
        item.OverlayBlend = Colors.White;
        item.OverlayDirection = Vector2.Up;
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
            var size = it.Texture.GetSize();
            var rect = new Rect2(-size * 0.5f, size);
            DrawTextureRect(it.Texture, rect, false, it.Modulate);

            if (it.OverlayIntensity > 0f)
            {
                DrawSetTransform(it.Position, it.Rotation, it.Scale);
                DrawGradientOverlay(rect, it);
            }
        }

        // Reset transform so debug overlays draw in world space
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // Draw debug primitives last so they appear over sprites
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
        var axis = item.OverlayDirection;
        if (axis == Vector2.Zero)
            axis = Vector2.Up;
        axis = axis.Normalized();

        var vertices = new Vector2[4]
        {
            rect.Position,
            rect.Position + new Vector2(rect.Size.X, 0f),
            rect.Position + rect.Size,
            rect.Position + new Vector2(0f, rect.Size.Y)
        };

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

        var colors = new Color[4];
        for (int i = 0; i < vertices.Length; i++)
        {
            float t = Mathf.Clamp((vertices[i].Dot(axis) - min) / range, 0f, 1f);
            colors[i] = bottom.Lerp(top, t);
        }

        var uvs = new Vector2[4]
        {
            Vector2.Zero,
            new Vector2(1f, 0f),
            Vector2.One,
            new Vector2(0f, 1f)
        };

        DrawPolygon(vertices, colors, uvs, item.Texture);
    }
}

public static class CustomEntityRenderEngineLocator
{
    public static CustomEntityRenderEngine Renderer { get; set; }
}