using Godot;
using Game.Data;
using Game.Data.Components;
using System;

namespace Game.Utils;

/// <summary>
/// Visualizes the currently selected NPC with a blue debug box.
/// </summary>
public partial class NPCSelectionVisualizer : SingletonNode<NPCSelectionVisualizer>
{
    [Export] public Color SelectionColor = Colors.Cyan;
    [Export] public float BoxThickness = 3f;
    [Export] public float BoxHeight = 100f;
    [Export] public float BoxWidth = 100f;
    [Export] public Vector2 BoxOffset = Vector2.Zero;

    const float childHeightMultiplier = 0.75f;

    private Entity _selectedNPC;
    private CustomEntityRenderEngine _renderer;

    public override void _Ready()
    {
        base._Ready();

        // Subscribe to selection changes
        if (NPCSelectionManager.HasInstance)
        {
            NPCSelectionManager.Instance.OnNPCSelected += OnNPCSelectionChanged;
        }

        SetProcess(true);
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        // Unsubscribe from selection changes
        if (NPCSelectionManager.HasInstance)
        {
            NPCSelectionManager.Instance.OnNPCSelected -= OnNPCSelectionChanged;
        }
    }

    private void OnNPCSelectionChanged(Entity npc)
    {
        _selectedNPC = npc;
    }

    public override void _Process(double delta)
    {
        if (_renderer == null)
        {
            _renderer = CustomEntityRenderEngineLocator.Renderer;
        }

        if (_renderer == null || _selectedNPC == null)
            return;

        // Check if NPC still has required components
        if (!_selectedNPC.TryGetComponent<TransformComponent2D>(out var transform))
        {
            _selectedNPC = null;
            return;
        }
        var heightMultiplier = _selectedNPC.GetComponent<NPCData>().AgeGroup == NPCAgeGroup.Child ? childHeightMultiplier : 1f;
        var height = BoxHeight * heightMultiplier;

        var offset = new Vector2(BoxOffset.X, BoxOffset.Y * heightMultiplier);


        // Draw a blue rectangle around the NPC
        var position = transform.Position;
        var halfSize = new Vector2(BoxWidth / 2f, height / 2f);

        // Draw four lines to form a rectangle
        var topLeft = position + offset + new Vector2(-halfSize.X, -halfSize.Y);
        var topRight = position + offset + new Vector2(halfSize.X, -halfSize.Y);
        var bottomRight = position + offset + new Vector2(halfSize.X, halfSize.Y);
        var bottomLeft = position + offset + new Vector2(-halfSize.X, halfSize.Y);

        _renderer.QueueDebugLine(topLeft, topRight, SelectionColor, BoxThickness);
        _renderer.QueueDebugLine(topRight, bottomRight, SelectionColor, BoxThickness);
        _renderer.QueueDebugLine(bottomRight, bottomLeft, SelectionColor, BoxThickness);
        _renderer.QueueDebugLine(bottomLeft, topLeft, SelectionColor, BoxThickness);
    }
}
