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

    public override void _Draw()
    {
        // Draw queued debug arrows in world space first
        for (int i = 0; i < _debugArrows.Count; i++)
        {
            var a = _debugArrows[i];
            // Line
            DrawLine(a.From, a.To, a.Color, a.Thickness);

            // Arrowhead
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
        // Lines
        for (int i = 0; i < _debugLines.Count; i++)
        {
            var l = _debugLines[i];
            DrawLine(l.From, l.To, l.Color, l.Thickness);
        }

        // Circles (as arcs)
        for (int i = 0; i < _debugCircles.Count; i++)
        {
            var c = _debugCircles[i];
            DrawArc(c.Center, c.Radius, 0f, Mathf.Tau, c.Segments, c.Color, c.Thickness);
        }

        _debugArrows.Clear();
        _debugLines.Clear();
        _debugCircles.Clear();

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
            var half = it.Texture.GetSize() * 0.5f;
            DrawTexture(it.Texture, -half, it.Modulate);
        }
    }
}

public static class CustomEntityRenderEngineLocator
{
    public static CustomEntityRenderEngine Renderer { get; set; }
}


