using Godot;
using Game.Utils;
using Game.Data;
using Game.Universe;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;

namespace Game.Utils;

public partial class EntityDebugTooltip : SingletonNode<EntityDebugTooltip>
{
    [Export] public bool OverlayEnabled = true;
    [Export] public float UpdatesPerSecond = 4.0f;
    [Export] public int MarginX = 8;
    [Export] public int MarginY = 8;
    [Export] public float ProximityThreshold = 35.0f;

    private Label _label;
    private CanvasLayer _layer;
    private PanelContainer _panel;
    private VBoxContainer _content;
    private float _accumulator;
    private float _thresholdSq => ProximityThreshold * ProximityThreshold;

    public override void _Ready()
    {
        base._Ready();

        _layer = new CanvasLayer { Name = "EntityDebugLayer" };
        _layer.Layer = 100;
        AddChild(_layer);

        _panel = new PanelContainer { Name = "EntityDebugPanel" };
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _panel.OffsetTop = MarginY;
        _panel.OffsetLeft = MarginX;
        _panel.Visible = false;
        _layer.AddChild(_panel);

        _content = new VBoxContainer { Name = "Content" };
        _content.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(_content);

        _label = new Label { Name = "DebugLabel" };
        _label.MouseFilter = Control.MouseFilterEnum.Ignore;
        _label.HorizontalAlignment = HorizontalAlignment.Left;
        _content.AddChild(_label);

        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!OverlayEnabled)
        {
            if (_panel.Visible)
                _panel.Visible = false;
            return;
        }

        var interval = 1.0f / Mathf.Max(1.0f, UpdatesPerSecond);
        _accumulator += (float)delta;
        if (_accumulator < interval)
            return;

        _accumulator = 0.0f;

        // Require a cached world mouse position
        if (ViewContext.CachedMouseGlobalPosition is not Vector2 mouseWorld)
        {
            if (_panel.Visible)
                _panel.Visible = false;
            return;
        }

        var entity = FindClosestEntity(mouseWorld);
        if (entity == null)
        {
            if (_panel.Visible)
                _panel.Visible = false;
            return;
        }

        // Build text for the selected entity
        _label.Text = BuildEntityText(entity);

        // Position near cursor (screen space)
        var mouseScreen = GetViewport().GetMousePosition();
        _panel.OffsetLeft = (int)(mouseScreen.X + MarginX);
        _panel.OffsetTop = (int)(mouseScreen.Y + MarginY);
        if (!_panel.Visible)
            _panel.Visible = true;
    }

    private Data.Entity FindClosestEntity(Vector2 mouseWorld)
    {
        var manager = Universe.EntityManager.Instance;
        if (manager == null) return null;

        Data.Entity best = null;
        float bestDistSq = float.MaxValue;

        var list = manager.GetEntities();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not Data.Entity e) continue;
            var t = e.Transform;
            if (t == null) continue;
            float d2 = (t.Position - mouseWorld).LengthSquared();
            if (d2 <= _thresholdSq && d2 < bestDistSq)
            {
                bestDistSq = d2;
                best = e;
            }
        }

        return best;
    }

    private string BuildEntityText(Data.Entity entity)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine($"Name: {entity.Name}");
        sb.AppendLine($"Id: {entity.Id}");

        // Tags
        if (entity.Tags != null)
        {
            var tags = string.Join(", ", entity.Tags.Select(t => t.ToString()));
            sb.AppendLine($"Tags: [{tags}]");
        }

        // Position (if available)
        var tr = entity.Transform;
        if (tr != null)
        {
            sb.AppendLine($"Position: {FormatValue(tr.Position)}  Rot: {tr.Rotation:0.###}  Scale: {FormatValue(tr.Scale)}");
        }

        // Components
        foreach (var comp in entity.GetAllComponents())
        {
            if (comp == null) continue;
            var type = comp.GetType();
            sb.AppendLine("");
            sb.AppendLine($"[{type.Name}]");

            // Public fields
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var f in fields)
            {
                object val;
                try { val = f.GetValue(comp); }
                catch { continue; }
                sb.AppendLine($"  {f.Name}: {FormatValue(val)}");
            }

            // Public readable properties (skip indexers)
            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead);
            foreach (var p in props)
            {
                object val;
                try { val = p.GetValue(comp); }
                catch { continue; }
                sb.AppendLine($"  {p.Name}: {FormatValue(val)}");
            }
        }

        return sb.ToString();
    }

    private string FormatValue(object value)
    {
        if (value == null) return "null";

        switch (value)
        {
            case string s:
                return s;
            case bool b:
                return b ? "true" : "false";
            case Enum e:
                return e.ToString();
            case int or long or short or byte or uint or ulong or ushort:
                return Convert.ToString(value);
            case float f:
                return f.ToString("0.###");
            case double d:
                return d.ToString("0.###");
            case Vector2 v2:
                return $"({v2.X:0.###}, {v2.Y:0.###})";
            case Vector3 v3:
                return $"({v3.X:0.###}, {v3.Y:0.###}, {v3.Z:0.###})";
            case Color c:
                return c.ToHtml();
            case GodotObject go:
                return go.GetType().Name;
            default:
                return value.ToString();
        }
    }
}