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

    private CanvasLayer _layer;
    private PanelContainer _panel;
    private TabContainer _tabs;

    // Tab 1: Status
    private VBoxContainer _statusContainer;
    private Label _statsLabel;

    // Tab 2: Inventory
    private VBoxContainer _inventoryContainer;
    private Label _inventoryLabel;

    // Tab 3: Brain
    private VBoxContainer _brainContainer;
    private Label _goalLabel;
    private VBoxContainer _planStepsContainer;
    private Label _utilitiesLabel;

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
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
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
        _statusContainer = new VBoxContainer { Name = "Status" };
        _statusContainer.AddThemeConstantOverride("separation", 10);
        _statusContainer.AddThemeConstantOverride("margin_top", 10);
        _statusContainer.AddThemeConstantOverride("margin_left", 5);
        _statusContainer.AddThemeConstantOverride("margin_right", 5);
        _tabs.AddChild(_statusContainer);

        _statsLabel = new Label();
        _statusContainer.AddChild(_statsLabel);

        // --- Tab 2: Inventory ---
        _inventoryContainer = new VBoxContainer { Name = "Inventory" };
        _inventoryContainer.AddThemeConstantOverride("margin_top", 10);
        _inventoryContainer.AddThemeConstantOverride("margin_left", 5);
        _inventoryContainer.AddThemeConstantOverride("margin_right", 5);
        _tabs.AddChild(_inventoryContainer);

        _inventoryLabel = new Label();
        _inventoryContainer.AddChild(_inventoryLabel);

        // --- Tab 3: Brain ---
        _brainContainer = new VBoxContainer { Name = "Brain" };
        _brainContainer.AddThemeConstantOverride("margin_top", 10);
        _brainContainer.AddThemeConstantOverride("margin_left", 5);
        _brainContainer.AddThemeConstantOverride("margin_right", 5);
        _tabs.AddChild(_brainContainer);

        _goalLabel = new Label();
        _goalLabel.AddThemeFontSizeOverride("font_size", 16);
        _brainContainer.AddChild(_goalLabel);

        _brainContainer.AddChild(new HSeparator());
        _brainContainer.AddChild(new Label { Text = "Current Plan:", ThemeTypeVariation = "HeaderSmall" });

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(0, 200);
        _brainContainer.AddChild(scroll);

        _planStepsContainer = new VBoxContainer();
        scroll.AddChild(_planStepsContainer);

        _brainContainer.AddChild(new HSeparator());
        _utilitiesLabel = new Label();
        _brainContainer.AddChild(_utilitiesLabel);

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

        // Update Tab Title (hacky way to update window title if we had one, but here just debug)
        // _titleLabel.Text = ... (already set in header)

        if (_selectedNPC.TryGetComponent<NPCData>(out var npcData))
        {
            UpdateStatusTab(npcData);
            UpdateInventoryTab(npcData);
        }

        UpdateBrainTab();
    }

    private void UpdateStatusTab(NPCData npcData)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Name: {_selectedNPC.Name}");
        sb.AppendLine($"ID:   {_selectedNPC.Id}");
        sb.AppendLine();
        sb.AppendLine($"❤️  Health:      {FormatBar(npcData.Health, npcData.MaxHealth)}");
        sb.AppendLine($"🍖 Hunger:      {FormatBar(npcData.Hunger, npcData.MaxHunger)}");
        sb.AppendLine($"💧 Thirst:      {FormatBar(npcData.Thirst, npcData.MaxThirst)}");
        sb.AppendLine($"😴 Sleepiness:  {FormatBar(npcData.Sleepiness, npcData.MaxSleepiness)}");
        sb.AppendLine($"😊 Happiness:   {FormatBar(npcData.Happiness, npcData.MaxHappiness)}");
        sb.AppendLine($"🌡️  Temperature: {FormatBar(npcData.Temperature, 100f)}");
        sb.AppendLine($"💕 MatingDesire: {FormatBar(npcData.MatingDesire, 100f)}");

        if (npcData.IncomingMateRequestStatus != NPCData.MateRequestStatus.None)
        {
            sb.AppendLine($"💌 Request: {npcData.IncomingMateRequestStatus} (from {npcData.IncomingMateRequestFrom.ToString().Substring(0, 8)}...)");
        }
        if (npcData.IsOnMateCooldown)
        {
            sb.AppendLine($"⏳ Mate Cooldown: {(npcData.MateCooldownUntil - Time.GetTicksMsec() / 1000.0):F1}s");
        }

        _statsLabel.Text = sb.ToString();
    }

    private void UpdateInventoryTab(NPCData npcData)
    {
        var sb = new StringBuilder();
        if (npcData.Resources.Count == 0)
        {
            sb.AppendLine("\n(Inventory is empty)");
        }
        else
        {
            foreach (var kvp in npcData.Resources.OrderBy(x => x.Key.ToString()))
            {
                var icon = GetResourceIcon(kvp.Key);
                sb.AppendLine($"{icon} {kvp.Key}: {kvp.Value}");
            }
        }
        _inventoryLabel.Text = sb.ToString();
    }

    private void UpdateBrainTab()
    {
        var hasSelector = _selectedNPC.TryGetComponent<UtilityGoalSelector>(out var selector);
        var hasExecutor = _selectedNPC.TryGetComponent<AIGoalExecutor>(out var executor);

        if (!hasSelector && !hasExecutor)
        {
            _goalLabel.Text = "No AI Components";
            return;
        }

        // Reflection to get private fields
        Data.UtilityAI.IUtilityGoal currentGoal = null;
        List<Data.UtilityAI.IUtilityGoal> availableGoals = null;
        Data.GOAP.Plan currentPlan = null;
        int currentStepIndex = -1;

        if (hasSelector && selector != null)
        {
            var selType = typeof(UtilityGoalSelector);
            currentGoal = selType.GetField("_currentGoal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(selector) as Data.UtilityAI.IUtilityGoal;
            availableGoals = selType.GetField("_availableGoals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(selector) as List<Data.UtilityAI.IUtilityGoal>;
        }

        if (hasExecutor && executor != null)
        {
            var execType = typeof(AIGoalExecutor);
            currentPlan = execType.GetField("_currentPlan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(executor) as Data.GOAP.Plan;
        }

        if (currentPlan != null)
        {
            var planType = typeof(Data.GOAP.Plan);
            var idxObj = planType.GetField("_currentStepIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(currentPlan);
            if (idxObj is int idx) currentStepIndex = idx;
        }

        // Update Goal Label
        if (currentGoal != null)
        {
            float util = currentGoal.CalculateUtility(_selectedNPC);
            _goalLabel.Text = $"🎯 {currentGoal.Name} ({util:F2})";
        }
        else
        {
            _goalLabel.Text = "🎯 No Active Goal";
        }

        // Update Plan Visualization
        foreach (var child in _planStepsContainer.GetChildren()) child.QueueFree();

        if (currentPlan != null && !currentPlan.IsComplete)
        {
            for (int i = 0; i < currentPlan.Steps.Count; i++)
            {
                var step = currentPlan.Steps[i];
                var stepLabel = new Label();

                string prefix = "   ";
                Color color = new Color(0.6f, 0.6f, 0.6f); // Pending (gray)

                if (i < currentStepIndex)
                {
                    prefix = "✓ ";
                    color = new Color(0.4f, 0.8f, 0.4f); // Completed (green)
                }
                else if (i == currentStepIndex)
                {
                    prefix = "▶ ";
                    color = new Color(1f, 0.9f, 0.3f); // Current (yellow)
                    stepLabel.AddThemeColorOverride("font_color", color);
                }

                stepLabel.Text = $"{prefix}{step.Name}";
                stepLabel.Modulate = color;
                _planStepsContainer.AddChild(stepLabel);
            }
        }
        else if (currentPlan != null && currentPlan.Succeeded)
        {
            _planStepsContainer.AddChild(new Label { Text = "Plan Completed Successfully", Modulate = Colors.Green });
        }
        else if (currentPlan != null)
        {
            _planStepsContainer.AddChild(new Label { Text = "Plan Failed", Modulate = Colors.Red });
        }
        else
        {
            _planStepsContainer.AddChild(new Label { Text = "(No active plan)", Modulate = Colors.Gray });
        }

        // Update Utilities
        var sb = new StringBuilder();
        if (hasExecutor && executor.PlanningInProgress)
        {
            sb.AppendLine("Current Plan: 🧠 Planning...");
        }
        else if (currentPlan == null)
        {
            sb.AppendLine("Current Plan: (No active plan)");
        }
        else if (currentPlan.IsComplete && currentPlan.Succeeded)
        {
            sb.AppendLine("Current Plan: ✅ Completed");
        }
        else if (currentPlan.IsComplete && !currentPlan.Succeeded)
        {
            sb.AppendLine("Current Plan: ❌ Failed");
        }
        else
        {
            sb.AppendLine("Current Plan: 🏃 Active");
        }
        sb.AppendLine(); // Add a blank line for separation

        if (availableGoals != null)
        {
            sb.AppendLine("Goal Utilities:");
            var utilities = availableGoals
                .Select(g => new { Goal = g, Utility = g.CalculateUtility(_selectedNPC) })
                .OrderByDescending(x => x.Utility)
                .ToList();

            foreach (var u in utilities)
            {
                var bar = FormatMiniBar(u.Utility, 1.0f);
                var marker = u.Goal == currentGoal ? "▶" : " ";
                sb.AppendLine($" {marker} {u.Goal.Name}: {bar} {u.Utility:F2}");
            }
        }
        _utilitiesLabel.Text = sb.ToString();
    }

    private string FormatBar(float value, float max)
    {
        float ratio = Mathf.Clamp(value / max, 0f, 1f);
        int barLength = 15;
        int filled = (int)(ratio * barLength);
        string bar = new string('█', filled) + new string('░', barLength - filled);
        return $"{bar} {value:F0}/{max:F0}";
    }

    private string FormatMiniBar(float value, float max)
    {
        float ratio = Mathf.Clamp(value / max, 0f, 1f);
        int barLength = 8;
        int filled = (int)(ratio * barLength);
        return new string('█', filled) + new string('░', barLength - filled);
    }

    private string GetResourceIcon(TargetType type)
    {
        return type switch
        {
            TargetType.Stick => "🪵",
            TargetType.Food => "🍖",
            TargetType.Tree => "🌳",
            TargetType.Campfire => "🔥",
            TargetType.Bed => "🛏️",
            _ => "📦"
        };
    }
}
