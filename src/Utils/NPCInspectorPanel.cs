using Godot;
using Game.Utils;
using Game.Data;
using Game.Data.Components;
using Game.Universe;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace Game.Utils;

/// <summary>
/// UI panel that displays detailed NPC information when an NPC is clicked.
/// Occupies the right 1/4th of the screen.
/// </summary>
public partial class NPCInspectorPanel : SingletonNode<NPCInspectorPanel>
{
    [Export] public bool Enabled = true;
    [Export] public float UpdatesPerSecond = 10.0f;

    // Font Size Constants
    public const int FontSizeSmall = 14;
    public const int FontSizeNormal = 18;
    public const int FontSizeHeader = 24;
    public const int FontSizeTitle = 32;
    public const int FontSizeIcon = 40;

    private CanvasLayer _layer;
    private PanelContainer _panel;
    private TabContainer _tabs;

    // Tabs
    private NPCInspectorStatusTab _statusTab;
    private NPCInspectorInventoryTab _inventoryTab;
    private NPCInspectorBrainTab _brainTab;

    private Entity _selectedNPC;
    private float _updateAccumulator;
    private Camera2D _camera;

    public override void _Ready()
    {
        base._Ready();

        // Create UI layer
        _layer = new CanvasLayer { Name = "NPCInspectorLayer" };
        _layer.Layer = 99;
        AddChild(_layer);

        // Create panel container (right side, 1/4 screen width)
        _panel = new PanelContainer { Name = "NPCInspectorPanel" };
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        _panel.Visible = false;

        // Style the main panel
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.1f, 0.12f, 0.95f),
            BorderWidthLeft = 2,
            BorderColor = new Color(0.3f, 0.3f, 0.35f),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
        _panel.AddThemeStyleboxOverride("panel", style);

        _layer.AddChild(_panel);

        LM.Info("[NPCInspector] Panel created and ready");

        // Subscribe to NPC selection events (deferred to ensure manager is ready)
        CallDeferred(nameof(SubscribeToSelectionManager));

        // Main layout
        var mainVBox = new VBoxContainer();
        mainVBox.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(mainVBox);

        // Header with Close Button
        var header = new HBoxContainer();
        mainVBox.AddChild(header);

        var titleLabel = new Label
        {
            Text = "Inspector",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        titleLabel.AddThemeFontSizeOverride("font_size", FontSizeTitle);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 1f));
        header.AddChild(titleLabel);

        var closeBtn = new Button { Text = "✕" };
        closeBtn.CustomMinimumSize = new Vector2(30, 30);
        closeBtn.Pressed += () => { _selectedNPC = null; _panel.Visible = false; };
        header.AddChild(closeBtn);

        mainVBox.AddChild(new HSeparator());

        // Tabs
        _tabs = new TabContainer();
        _tabs.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _tabs.MouseFilter = Control.MouseFilterEnum.Stop; // Allow interaction with tabs
        mainVBox.AddChild(_tabs);

        // --- Tab 1: Status ---
        _statusTab = new NPCInspectorStatusTab();
        _tabs.AddChild(_statusTab);

        // --- Tab 2: Inventory ---
        _inventoryTab = new NPCInspectorInventoryTab();
        _tabs.AddChild(_inventoryTab);

        // --- Tab 3: Brain ---
        _brainTab = new NPCInspectorBrainTab();
        _tabs.AddChild(_brainTab);

        SetProcess(true);
    }

    private void SubscribeToSelectionManager()
    {
        if (NPCSelectionManager.HasInstance)
        {
            NPCSelectionManager.Instance.OnNPCSelected += OnNPCSelectionChanged;
            LM.Info("[NPCInspector] Subscribed to NPCSelectionManager");
        }
        else
        {
            LM.Warning("[NPCInspector] NPCSelectionManager not found, retrying...");
            CallDeferred(nameof(SubscribeToSelectionManager));
        }
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        // Unsubscribe from selection events
        if (NPCSelectionManager.HasInstance)
        {
            NPCSelectionManager.Instance.OnNPCSelected -= OnNPCSelectionChanged;
        }
    }

    private void OnNPCSelectionChanged(Entity npc)
    {
        _selectedNPC = npc;
        _panel.Visible = npc != null;

        LM.Info($"[NPCInspector] Selection changed to: {(npc != null ? npc.Name : "null")}");

        // Set camera to follow the selected NPC
        if (_camera == null) _camera = GetViewport().GetCamera2D();
        if (_camera is CameraController2D cameraController)
        {
            cameraController.SetFollowTarget(npc);
            LM.Debug($"[NPCInspector] Camera follow set to: {(npc != null ? npc.Name : "null")}");
        }
        else
        {
            LM.Warning("[NPCInspector] Camera is not a CameraController2D!");
        }
    }

    public override void _Process(double delta)
    {
        if (!Enabled)
        {
            if (_panel.Visible) _panel.Visible = false;
            return;
        }

        var viewportSize = GetViewport().GetVisibleRect().Size;
        _panel.CustomMinimumSize = new Vector2(viewportSize.X * 0.25f, viewportSize.Y);

        if (_selectedNPC != null)
        {
            if (_selectedNPC.TryGetComponent<NPCData>(out _))
            {
                var updateInterval = 1.0f / Mathf.Max(1.0f, UpdatesPerSecond);
                _updateAccumulator += (float)delta;

                if (_updateAccumulator >= updateInterval)
                {
                    _updateAccumulator = 0f;
                    UpdatePanelContent();
                }

                if (!_panel.Visible) _panel.Visible = true;
            }
            else
            {
                _selectedNPC = null;
                _panel.Visible = false;
            }
        }
        else
        {
            if (_panel.Visible) _panel.Visible = false;
        }
    }

    private void UpdatePanelContent()
    {
        if (_selectedNPC == null) return;

        // Update Tabs
        _statusTab.UpdateTab(_selectedNPC);
        _inventoryTab.UpdateTab(_selectedNPC);
        _brainTab.UpdateTab(_selectedNPC);
    }
}
