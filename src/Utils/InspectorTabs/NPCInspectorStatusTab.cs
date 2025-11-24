using Godot;
using Game.Data;
using Game.Data.Components;
using System.Collections.Generic;

namespace Game.Utils;

public partial class NPCInspectorStatusTab : VBoxContainer
{
    private Label _nameLabel;
    private Label _idLabel;
    private Label _genderLabel;
    
    // Progress Bars dictionary for easy updating
    private Dictionary<string, ProgressBar> _statBars = new();
    private Dictionary<string, Label> _statValueLabels = new();
    
    // Detailed info area
    private Label _detailsLabel;

    public override void _Ready()
    {
        Name = "Status";
        AddThemeConstantOverride("separation", 12);
        AddThemeConstantOverride("margin_top", 15);
        AddThemeConstantOverride("margin_left", 10);
        AddThemeConstantOverride("margin_right", 10);
        AddThemeConstantOverride("margin_bottom", 15);

        // --- Header Section ---
        var headerContainer = new VBoxContainer();
        AddChild(headerContainer);

        var topRow = new HBoxContainer();
        headerContainer.AddChild(topRow);

        _nameLabel = new Label();
        _nameLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeHeader);
        _nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f)); // Gold color for name
        _nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        topRow.AddChild(_nameLabel);

        _genderLabel = new Label();
        _genderLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
        topRow.AddChild(_genderLabel);

        _idLabel = new Label();
        _idLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
        _idLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
        headerContainer.AddChild(_idLabel);

        AddChild(new HSeparator());

        // --- Stats Grid ---
        var statsGrid = new GridContainer();
        statsGrid.Columns = 2;
        statsGrid.AddThemeConstantOverride("h_separation", 15);
        statsGrid.AddThemeConstantOverride("v_separation", 8);
        AddChild(statsGrid);

        // Define stats to track
        CreateStatRow(statsGrid, "Health", Colors.Red, "❤️");
        CreateStatRow(statsGrid, "Hunger", Colors.Orange, "🍖");
        CreateStatRow(statsGrid, "Thirst", Colors.CornflowerBlue, "💧");
        CreateStatRow(statsGrid, "Sleepiness", Colors.MediumPurple, "😴");
        CreateStatRow(statsGrid, "Happiness", Colors.Yellow, "😊");
        CreateStatRow(statsGrid, "Temperature", Colors.OrangeRed, "🌡️");
        CreateStatRow(statsGrid, "MatingDesire", Colors.HotPink, "💕");

        AddChild(new HSeparator());

        // --- Details Section ---
        _detailsLabel = new Label();
        _detailsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailsLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
        _detailsLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        AddChild(_detailsLabel);
    }

    private void CreateStatRow(GridContainer parent, string key, Color color, string icon)
    {
        // Label
        var label = new Label();
        label.Text = $"{icon} {key}";
        label.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
        label.CustomMinimumSize = new Vector2(100, 0); // Fixed width for labels
        parent.AddChild(label);

        // Bar Container (to hold bar + value text)
        var barContainer = new VBoxContainer();
        barContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        barContainer.CustomMinimumSize = new Vector2(150, 0); // Min width for bars
        parent.AddChild(barContainer);

        // Progress Bar
        var progressBar = new ProgressBar();
        progressBar.ShowPercentage = false;
        progressBar.CustomMinimumSize = new Vector2(0, 14); // Thicker bar
        progressBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        
        // Custom Style for the bar
        var bgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.2f, 0.2f, 1f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };
        var fillStyle = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };
        
        progressBar.AddThemeStyleboxOverride("background", bgStyle);
        progressBar.AddThemeStyleboxOverride("fill", fillStyle);

        barContainer.AddChild(progressBar);
        _statBars[key] = progressBar;

        // Value Label (Small text under or overlaying)
        var valueLabel = new Label();
        valueLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        valueLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        barContainer.AddChild(valueLabel);
        _statValueLabels[key] = valueLabel;
    }

    public void UpdateTab(Entity selectedNPC)
    {
        if (selectedNPC == null) return;

        if (selectedNPC.TryGetComponent<NPCData>(out var npcData))
        {
            // Header
            _nameLabel.Text = selectedNPC.Name;
            _idLabel.Text = $"ID: {selectedNPC.Id}";
            
            string genderIcon = npcData.IsMale ? "♂" : "♀";
            Color genderColor = npcData.IsMale ? Colors.DeepSkyBlue : Colors.HotPink;
            _genderLabel.Text = $"{genderIcon} {npcData.AgeGroup}";
            _genderLabel.AddThemeColorOverride("font_color", genderColor);

            // Stats
            UpdateStat("Health", npcData.Health, npcData.MaxHealth);
            UpdateStat("Hunger", npcData.Hunger, npcData.MaxHunger);
            UpdateStat("Thirst", npcData.Thirst, npcData.MaxThirst);
            UpdateStat("Sleepiness", npcData.Sleepiness, npcData.MaxSleepiness);
            UpdateStat("Happiness", npcData.Happiness, npcData.MaxHappiness);
            UpdateStat("Temperature", npcData.Temperature, 100f);
            UpdateStat("MatingDesire", npcData.MatingDesire, 100f);

            // Details
            var sb = new System.Text.StringBuilder();
            if (npcData.IncomingMateRequestStatus != NPCData.MateRequestStatus.None)
            {
                sb.AppendLine($"💌 Request: {npcData.IncomingMateRequestStatus}");
                sb.AppendLine($"   From: {npcData.IncomingMateRequestFrom.ToString().Substring(0, 8)}...");
            }
            if (npcData.IsOnMateCooldown)
            {
                double remaining = npcData.MateCooldownUntil - Time.GetTicksMsec() / 1000.0;
                if (remaining > 0)
                    sb.AppendLine($"⏳ Mate Cooldown: {remaining:F1}s");
            }
            
            _detailsLabel.Text = sb.ToString();
        }
    }

    private void UpdateStat(string key, float value, float max)
    {
        if (_statBars.TryGetValue(key, out var bar))
        {
            bar.MaxValue = max;
            bar.Value = value;
        }

        if (_statValueLabels.TryGetValue(key, out var label))
        {
            label.Text = $"{value:F1} / {max:F1}";
        }
    }
}
