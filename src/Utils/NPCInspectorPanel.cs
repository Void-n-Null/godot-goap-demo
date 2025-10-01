using Godot;
using Game.Utils;
using Game.Data;
using Game.Data.Components;
using Game.Universe;
using System;
using System.Linq;
using System.Text;

namespace Game.Utils;

/// <summary>
/// UI panel that displays detailed NPC information when an NPC is clicked.
/// Occupies the right 1/4th of the screen.
/// </summary>
public partial class NPCInspectorPanel : SingletonNode<NPCInspectorPanel>
{
	[Export] public bool Enabled = true;
	[Export] public float UpdatesPerSecond = 10.0f;
	[Export] public float ClickRadius = 100f; // Larger radius for easier clicking

	private CanvasLayer _layer;
	private PanelContainer _panel;
	private VBoxContainer _content;
	private Label _titleLabel;
	private Label _statsLabel;
	private Label _inventoryLabel;
	private Label _aiLabel;
	
	private Entity _selectedNPC;
	private float _updateAccumulator;
	private Camera2D _camera;
	private float _clickRadiusSq => ClickRadius * ClickRadius;

	public override void _Ready()
	{
		base._Ready();

		// Create UI layer
		_layer = new CanvasLayer { Name = "NPCInspectorLayer" };
		_layer.Layer = 99;
		AddChild(_layer);

		// Create panel container (right side, 1/4 screen width)
		_panel = new PanelContainer { Name = "NPCInspectorPanel" };
		_panel.MouseFilter = Control.MouseFilterEnum.Ignore; // Don't block mouse events
		_panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
		_panel.GrowHorizontal = Control.GrowDirection.Begin;
		_panel.Visible = false;
		_layer.AddChild(_panel);
		
		GD.Print("[NPCInspector] Panel created and ready");

		// Add a close button
		var closeBtn = new Button { Text = "✕" };
		closeBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		closeBtn.Position = new Vector2(-30, 5);
		closeBtn.Size = new Vector2(25, 25);
		closeBtn.Pressed += () => { _selectedNPC = null; _panel.Visible = false; };
		_panel.AddChild(closeBtn);

		// Main content container
		_content = new VBoxContainer { Name = "Content" };
		_content.MouseFilter = Control.MouseFilterEnum.Ignore;
		_content.AddThemeConstantOverride("separation", 10);
		_panel.AddChild(_content);

		// Add padding
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 15);
		margin.AddThemeConstantOverride("margin_right", 15);
		margin.AddThemeConstantOverride("margin_top", 15);
		margin.AddThemeConstantOverride("margin_bottom", 15);
		_content.AddChild(margin);

		var innerContent = new VBoxContainer();
		innerContent.AddThemeConstantOverride("separation", 15);
		margin.AddChild(innerContent);

		// Title
		_titleLabel = new Label { Name = "TitleLabel" };
		_titleLabel.AddThemeFontSizeOverride("font_size", 18);
		innerContent.AddChild(_titleLabel);

		// Add separator
		var sep1 = new HSeparator();
		innerContent.AddChild(sep1);

		// Stats section
		var statsTitle = new Label { Text = "Stats" };
		statsTitle.AddThemeFontSizeOverride("font_size", 14);
		innerContent.AddChild(statsTitle);

		_statsLabel = new Label { Name = "StatsLabel" };
		_statsLabel.AddThemeFontSizeOverride("font_size", 12);
		innerContent.AddChild(_statsLabel);

		// Inventory section
		var sep2 = new HSeparator();
		innerContent.AddChild(sep2);

		var invTitle = new Label { Text = "Inventory" };
		invTitle.AddThemeFontSizeOverride("font_size", 14);
		innerContent.AddChild(invTitle);

		_inventoryLabel = new Label { Name = "InventoryLabel" };
		_inventoryLabel.AddThemeFontSizeOverride("font_size", 12);
		innerContent.AddChild(_inventoryLabel);

		// AI Behavior section
		var sep3 = new HSeparator();
		innerContent.AddChild(sep3);

		var aiTitle = new Label { Text = "AI Behavior" };
		aiTitle.AddThemeFontSizeOverride("font_size", 14);
		innerContent.AddChild(aiTitle);

		_aiLabel = new Label { Name = "AILabel" };
		_aiLabel.AddThemeFontSizeOverride("font_size", 12);
		innerContent.AddChild(_aiLabel);

		SetProcess(true);
		SetProcessUnhandledInput(true);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Enabled) return;

		// Only handle left mouse button clicks that haven't been consumed by UI
		if (@event is InputEventMouseButton mouseButton && 
		    mouseButton.ButtonIndex == MouseButton.Left && 
		    mouseButton.Pressed)
		{
			var mouseWorld = GetGlobalMousePosition();
			GD.Print($"[NPCInspector] Unhandled click at world pos: {mouseWorld}");
			
			var clickedNPC = FindNPCAtPosition(mouseWorld);
			
			if (clickedNPC != null)
			{
				GD.Print($"[NPCInspector] Found NPC: {clickedNPC.Name}");
				_selectedNPC = clickedNPC;
				_panel.Visible = true;
				GetViewport().SetInputAsHandled(); // Mark input as handled
			}
			else
			{
				GD.Print($"[NPCInspector] No NPC found within {ClickRadius} units");
			}
		}
	}

	public override void _Process(double delta)
	{
		if (!Enabled)
		{
			if (_panel.Visible)
				_panel.Visible = false;
			return;
		}

		// Update panel size to be 1/4 of screen width
		var viewportSize = GetViewport().GetVisibleRect().Size;
		_panel.CustomMinimumSize = new Vector2(viewportSize.X * 0.25f, viewportSize.Y);

		// Update selected NPC data
		if (_selectedNPC != null)
		{
			// Check if NPC still exists and has NPCData
			if (_selectedNPC.TryGetComponent<NPCData>(out _))
			{
				var updateInterval = 1.0f / Mathf.Max(1.0f, UpdatesPerSecond);
				_updateAccumulator += (float)delta;
				
				if (_updateAccumulator >= updateInterval)
				{
					_updateAccumulator = 0f;
					UpdatePanelContent();
				}

				if (!_panel.Visible)
					_panel.Visible = true;
			}
			else
			{
				// NPC no longer valid
				_selectedNPC = null;
				_panel.Visible = false;
			}
		}
		else
		{
			if (_panel.Visible)
				_panel.Visible = false;
		}
	}

	private Vector2 GetGlobalMousePosition()
	{
		if (_camera == null)
		{
			_camera = GetViewport().GetCamera2D();
			if (_camera != null)
				GD.Print("[NPCInspector] Camera found!");
		}

		if (_camera != null)
		{
			var mouseScreen = GetViewport().GetMousePosition();
			var worldPos = _camera.GetScreenCenterPosition() + (mouseScreen - GetViewport().GetVisibleRect().Size / 2);
			return worldPos;
		}

		// Fallback to cached position
		var fallback = ViewContext.CachedMouseGlobalPosition ?? Vector2.Zero;
		GD.Print($"[NPCInspector] Using fallback mouse position: {fallback}");
		return fallback;
	}

	private Entity FindNPCAtPosition(Vector2 worldPos)
	{
		var manager = EntityManager.Instance;
		if (manager == null)
		{
			GD.Print("[NPCInspector] EntityManager.Instance is null!");
			return null;
		}

		Entity closest = null;
		float closestDistSq = _clickRadiusSq;
		int npcCount = 0;
		int checkedCount = 0;

		foreach (var entity in manager.AllEntities)
		{
			if (entity is not Entity e) continue;
			checkedCount++;
			
			if (!e.TryGetComponent<NPCData>(out _)) continue;
			npcCount++;
			
			var transform = e.Transform;
			if (transform == null) continue;

			float distSq = (transform.Position - worldPos).LengthSquared();
			float dist = Mathf.Sqrt(distSq);
			
			if (distSq <= closestDistSq)
			{
				GD.Print($"[NPCInspector] NPC '{e.Name}' at distance {dist:F1} (threshold: {ClickRadius})");
				closestDistSq = distSq;
				closest = e;
			}
		}

		GD.Print($"[NPCInspector] Checked {checkedCount} entities, found {npcCount} NPCs");
		return closest;
	}

	private void UpdatePanelContent()
	{
		if (_selectedNPC == null) return;

		// Title
		_titleLabel.Text = $"📋 {_selectedNPC.Name}\nID: {_selectedNPC.Id.ToString()[..8]}...";

		// Stats
		if (_selectedNPC.TryGetComponent<NPCData>(out var npcData))
		{
			var statsSb = new StringBuilder();
			statsSb.AppendLine($"❤️  Health: {FormatBar(npcData.Health, npcData.MaxHealth)}");
			statsSb.AppendLine($"🍖 Hunger: {FormatBar(npcData.Hunger, npcData.MaxHunger)}");
			statsSb.AppendLine($"💧 Thirst: {FormatBar(npcData.Thirst, npcData.MaxThirst)}");
			statsSb.AppendLine($"😴 Sleepiness: {FormatBar(npcData.Sleepiness, npcData.MaxSleepiness)}");
			statsSb.AppendLine($"😊 Happiness: {FormatBar(npcData.Happiness, npcData.MaxHappiness)}");
			statsSb.AppendLine($"🌡️  Temperature: {FormatBar(npcData.Temperature, 100f)}");
			_statsLabel.Text = statsSb.ToString();

			// Inventory
			var invSb = new StringBuilder();
			if (npcData.Resources.Count == 0)
			{
				invSb.AppendLine("(empty)");
			}
			else
			{
				foreach (var kvp in npcData.Resources.OrderBy(x => x.Key.ToString()))
				{
					var icon = GetResourceIcon(kvp.Key);
					invSb.AppendLine($"{icon} {kvp.Key}: {kvp.Value}");
				}
			}
			_inventoryLabel.Text = invSb.ToString();
		}

		// AI Behavior
		if (_selectedNPC.TryGetComponent<UtilityAIBehaviorV2>(out var aiBehavior))
		{
			var aiSb = new StringBuilder();
			
			// Use reflection to get current goal and plan info
			var currentGoalField = typeof(UtilityAIBehaviorV2).GetField("_currentGoal", 
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var currentPlanField = typeof(UtilityAIBehaviorV2).GetField("_currentPlan",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var availableGoalsField = typeof(UtilityAIBehaviorV2).GetField("_availableGoals",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			
			var currentGoal = currentGoalField?.GetValue(aiBehavior) as Data.UtilityAI.IUtilityGoal;
			var currentPlan = currentPlanField?.GetValue(aiBehavior) as Data.GOAP.Plan;
			var availableGoals = availableGoalsField?.GetValue(aiBehavior) as System.Collections.Generic.List<Data.UtilityAI.IUtilityGoal>;

			// Current goal
			if (currentGoal != null)
			{
				aiSb.AppendLine($"🎯 Active Goal: {currentGoal.Name}");
				var utility = currentGoal.CalculateUtility(_selectedNPC);
				aiSb.AppendLine($"   Utility: {utility:F2}");
			}
			else
			{
				aiSb.AppendLine($"🎯 Active Goal: None");
			}

			aiSb.AppendLine();

			// Current plan
			if (currentPlan != null && !currentPlan.IsComplete)
			{
				aiSb.AppendLine($"📋 Plan: {currentPlan.Steps.Count} steps");
				aiSb.AppendLine($"   Status: In Progress");
			}
			else if (currentPlan != null && currentPlan.Succeeded)
			{
				aiSb.AppendLine("📋 Plan: Completed ✓");
			}
			else if (currentPlan != null)
			{
				aiSb.AppendLine("📋 Plan: Failed ✗");
			}
			else
			{
				aiSb.AppendLine("📋 Plan: (none)");
			}

			aiSb.AppendLine();

			// Goal utilities
			if (availableGoals != null && availableGoals.Count > 0)
			{
				aiSb.AppendLine("📊 Goal Utilities:");
				var utilities = availableGoals
					.Select(g => new { Goal = g, Utility = g.CalculateUtility(_selectedNPC) })
					.OrderByDescending(x => x.Utility)
					.ToList();

				foreach (var u in utilities)
				{
					var bar = FormatMiniBar(u.Utility, 1.0f);
					var marker = u.Goal == currentGoal ? "▶" : " ";
					aiSb.AppendLine($" {marker} {u.Goal.Name}: {bar} {u.Utility:F2}");
				}
			}

			_aiLabel.Text = aiSb.ToString();
		}
		else
		{
			_aiLabel.Text = "(No AI behavior)";
		}
	}

	private string FormatBar(float value, float max)
	{
		float ratio = Mathf.Clamp(value / max, 0f, 1f);
		int barLength = 10;
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

