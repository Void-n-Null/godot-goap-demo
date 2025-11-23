using Godot;
using Game.Data;
using Game.Data.Components;
using Game.Universe;
using System;

namespace Game.Utils;

/// <summary>
/// Centralized manager for NPC selection.
/// Handles input for clicking NPCs and notifies subscribers when selection changes.
/// </summary>
public partial class NPCSelectionManager : SingletonNode<NPCSelectionManager>
{
    [Export] public bool Enabled = true;
    [Export] public float ClickRadius = 100f;

    /// <summary>
    /// Event fired when an NPC is selected or deselected.
    /// Passes the selected Entity (or null if deselected).
    /// </summary>
    public event Action<Entity> OnNPCSelected;

    private Entity _selectedNPC;
    private Camera2D _camera;

    /// <summary>
    /// The currently selected NPC entity.
    /// </summary>
    public Entity SelectedNPC
    {
        get => _selectedNPC;
        private set
        {
            if (_selectedNPC != value)
            {
                _selectedNPC = value;
                OnNPCSelected?.Invoke(_selectedNPC);
            }
        }
    }

    public override void _Ready()
    {
        base._Ready();
        SetProcessUnhandledInput(true);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled) return;

        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left &&
            mouseButton.Pressed)
        {
            var mouseWorld = MousePos();
            var clickedNPC = FindNPCAtPosition(mouseWorld);

            if (clickedNPC != null)
            {
                SelectedNPC = clickedNPC;
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private Vector2 MousePos()
    {
        if (ViewContext.CachedMouseGlobalPosition is Vector2 mouse)
            return mouse;

        if (_camera == null) _camera = GetViewport().GetCamera2D();
        return _camera?.GetGlobalMousePosition() ?? Vector2.Zero;
    }

    private Entity FindNPCAtPosition(Vector2 worldPos)
    {
        var manager = EntityManager.Instance;
        if (manager == null) return null;

        float zoomScale = 1.0f;
        if (_camera != null) zoomScale = _camera.Zoom.X;

        float adjustedRadius = ClickRadius / zoomScale;
        float thresholdSq = adjustedRadius * adjustedRadius;

        Entity best = null;
        float bestDistSq = float.MaxValue;

        foreach (var entity in manager.AllEntities)
        {
            if (entity is not Entity e) continue;
            if (!e.TryGetComponent<NPCData>(out _)) continue;

            var transform = e.Transform;
            if (transform == null) continue;

            float distSq = (transform.Position - worldPos).LengthSquared();

            if (distSq <= thresholdSq && distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = e;
            }
        }
        return best;
    }

    /// <summary>
    /// Manually set the selected NPC (useful for programmatic selection).
    /// </summary>
    public void SelectNPC(Entity npc)
    {
        SelectedNPC = npc;
    }

    /// <summary>
    /// Clear the current selection.
    /// </summary>
    public void ClearSelection()
    {
        SelectedNPC = null;
    }
}
