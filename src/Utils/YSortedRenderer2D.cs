using System;
using System.Collections.Generic;
using Godot;

namespace Game.Utils;

/// <summary>
/// Immediate-mode 2D renderer that draws many sprites without per-entity nodes.
/// Sprites are globally sorted by world Y each frame (or when dirty) for painter's-order depth.
/// </summary>
public partial class YSortedRenderer2D : Node2D
{
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
    }

    private readonly List<Item> _items = new();
    private readonly Dictionary<SpriteKey, Item> _lookup = new();
    private ulong _nextId = 1;
    private bool _orderDirty = true;

    [Export] public bool AlwaysRedraw = true; // set false if you want event-driven redraws

    public override void _Ready()
    {
        base._Ready();
        YSortedRendererLocator.Renderer = this;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (YSortedRendererLocator.Renderer == this)
            YSortedRendererLocator.Renderer = null;
    }

    public override void _Process(double delta)
    {
        if (AlwaysRedraw)
            QueueRedraw();
    }

    public ulong AddSprite(Texture2D texture, Vector2 position, float rotation = 0f, Vector2? scale = null, Color? modulate = null)
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
            Modulate = modulate ?? Colors.White
        };
        _items.Add(item);
        _lookup[new SpriteKey(id)] = item;
        _orderDirty = true;
        return id;
    }

    public void UpdateSprite(ulong id, Vector2 position, float rotation, Vector2 scale)
    {
        if (!_lookup.TryGetValue(new SpriteKey(id), out var item)) return;
        // Mark order dirty only if Y changed
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

    public void RemoveSprite(ulong id)
    {
        var key = new SpriteKey(id);
        if (!_lookup.TryGetValue(key, out var item)) return;
        _lookup.Remove(key);
        // swap-remove for O(1)
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
        if (_orderDirty)
        {
            _items.Sort((a, b) => a.Position.Y.CompareTo(b.Position.Y));
            _orderDirty = false;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (it.Texture == null) continue;

            DrawSetTransform(it.Position, it.Rotation, it.Scale);
            // Centered draw: offset by half texture size
            var half = it.Texture.GetSize() * 0.5f;
            DrawTexture(it.Texture, -half, it.Modulate);
        }
    }
}

public static class YSortedRendererLocator
{
    public static YSortedRenderer2D Renderer { get; set; }
}


