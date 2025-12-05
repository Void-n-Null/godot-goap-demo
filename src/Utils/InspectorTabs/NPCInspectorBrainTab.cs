using Godot;
using Game.Data;
using Game.Data.Components;
using System.Linq;
using System.Collections.Generic;

namespace Game.Utils;

public partial class NPCInspectorBrainTab : VBoxContainer
{
    // --- UI Components ---
    private Label _currentGoalNameLabel;
    private Label _currentGoalScoreLabel;
    private ProgressBar _currentGoalUtilityBar;
    
    private VBoxContainer _planContainer;
    private VBoxContainer _utilitiesListContainer;
    
    // Cache for utility bars to avoid rebuilding every frame
    // Key: Goal Name
    private Dictionary<string, UtilityRow> _utilityRows = new();

    private partial class UtilityRow : HBoxContainer
    {
        private Label _nameLabel;
        private ProgressBar _bar;
        private Label _valueLabel;

        public UtilityRow()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddThemeConstantOverride("separation", 10);

            _nameLabel = new Label();
            _nameLabel.CustomMinimumSize = new Vector2(120, 0);
            _nameLabel.ClipText = true;
            _nameLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
            _nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
            AddChild(_nameLabel);

            _bar = new ProgressBar();
            _bar.ShowPercentage = false;
            _bar.CustomMinimumSize = new Vector2(0, 8);
            _bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            
            var bgStyle = new StyleBoxFlat { BgColor = new Color(0.2f, 0.2f, 0.25f), CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 };
            var fillStyle = new StyleBoxFlat { BgColor = new Color(0.4f, 0.6f, 0.9f), CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 };
            _bar.AddThemeStyleboxOverride("background", bgStyle);
            _bar.AddThemeStyleboxOverride("fill", fillStyle);
            AddChild(_bar);

            _valueLabel = new Label();
            _valueLabel.CustomMinimumSize = new Vector2(40, 0);
            _valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _valueLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
            _valueLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
            AddChild(_valueLabel);
        }

        public void Update(string name, float score, bool isActive)
        {
            _nameLabel.Text = name;
            _bar.Value = score;
            _bar.MaxValue = 1.0f; // Assuming utility is normalized 0-1, or adjust if not
            _valueLabel.Text = score.ToString("F2");

            // Highlight active goal
            if (isActive)
            {
                _nameLabel.AddThemeColorOverride("font_color", Colors.Gold);
                ((StyleBoxFlat)_bar.GetThemeStylebox("fill")).BgColor = Colors.Gold;
            }
            else
            {
                _nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
                ((StyleBoxFlat)_bar.GetThemeStylebox("fill")).BgColor = new Color(0.4f, 0.6f, 0.9f);
            }
        }
    }

    public override void _Ready()
    {
        Name = "Brain";
        AddThemeConstantOverride("separation", 15);
        AddThemeConstantOverride("margin_top", 15);
        AddThemeConstantOverride("margin_left", 10);
        AddThemeConstantOverride("margin_right", 10);
        AddThemeConstantOverride("margin_bottom", 15);

        // --- Active Goal Section (Fixed Height) ---
        var goalSection = new VBoxContainer();
        AddChild(goalSection);

        var goalHeaderLabel = new Label { Text = "Current Goal" };
        goalHeaderLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
        goalHeaderLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        goalSection.AddChild(goalHeaderLabel);

        // Goal Card Background
        var goalCard = new PanelContainer();
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.18f, 0.18f, 0.22f),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
        goalCard.AddThemeStyleboxOverride("panel", cardStyle);
        goalSection.AddChild(goalCard);

        var goalCardVBox = new VBoxContainer();
        goalCard.AddChild(goalCardVBox);

        // Top row: Goal Name + Score
        var goalTopRow = new HBoxContainer();
        goalCardVBox.AddChild(goalTopRow);

        _currentGoalNameLabel = new Label { Text = "Idle" };
        _currentGoalNameLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeHeader);
        _currentGoalNameLabel.AddThemeColorOverride("font_color", Colors.White);
        _currentGoalNameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        goalTopRow.AddChild(_currentGoalNameLabel);

        _currentGoalScoreLabel = new Label { Text = "0.00" };
        _currentGoalScoreLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
        _currentGoalScoreLabel.AddThemeColorOverride("font_color", Colors.Gold);
        goalTopRow.AddChild(_currentGoalScoreLabel);

        // Utility Bar under the name
        _currentGoalUtilityBar = new ProgressBar();
        _currentGoalUtilityBar.ShowPercentage = false;
        _currentGoalUtilityBar.CustomMinimumSize = new Vector2(0, 4);
        var activeBarStyle = new StyleBoxFlat { BgColor = Colors.Gold, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 };
        var activeBgStyle = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.1f), CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 };
        _currentGoalUtilityBar.AddThemeStyleboxOverride("fill", activeBarStyle);
        _currentGoalUtilityBar.AddThemeStyleboxOverride("background", activeBgStyle);
        _currentGoalUtilityBar.MaxValue = 1.0f;
        goalCardVBox.AddChild(_currentGoalUtilityBar);

        AddChild(new HSeparator());

        // --- Utilities Comparison Section (Fixed/Shrink Height) ---
        var utilsLabel = new Label { Text = "Goal Evaluation" };
        utilsLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
        utilsLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        AddChild(utilsLabel);

        _utilitiesListContainer = new VBoxContainer();
        _utilitiesListContainer.AddThemeConstantOverride("separation", 4);
        // Don't expand vertically, just take what it needs
        AddChild(_utilitiesListContainer);

        AddChild(new HSeparator());

        // --- Plan Execution Section (Expand Fill - Takes remaining space) ---
        var planLabel = new Label { Text = "Execution Plan" };
        planLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
        planLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        AddChild(planLabel);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill; // Expand to fill rest of tab
        
        // Background for scroll area
        var listBg = new PanelContainer();
        listBg.SizeFlagsVertical = SizeFlags.ExpandFill; // Also expand wrapper
        var listStyle = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.1f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        listBg.AddThemeStyleboxOverride("panel", listStyle);
        listBg.AddChild(scroll);
        AddChild(listBg);

        var contentMargin = new MarginContainer();
        contentMargin.AddThemeConstantOverride("margin_top", 10);
        contentMargin.AddThemeConstantOverride("margin_bottom", 10);
        contentMargin.AddThemeConstantOverride("margin_left", 10);
        contentMargin.AddThemeConstantOverride("margin_right", 10);
        contentMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(contentMargin);

        _planContainer = new VBoxContainer();
        _planContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _planContainer.AddThemeConstantOverride("separation", 5);
        contentMargin.AddChild(_planContainer);
    }

    public void UpdateTab(Entity selectedNPC)
    {
        if (selectedNPC == null) return;

        var hasSelector = selectedNPC.TryGetComponent<UtilityGoalSelector>(out var selector);
        var hasExecutor = selectedNPC.TryGetComponent<AIGoalExecutor>(out var executor);

        if (!hasSelector && !hasExecutor)
        {
            _currentGoalNameLabel.Text = "No AI Components";
            return;
        }

        // --- Reflection to get internal state ---
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
            currentPlan = executor.ActivePlan;
            currentStepIndex = executor.ActivePlanStepIndex;
        }

        // --- Update Active Goal Card ---
        if (currentGoal != null)
        {
            float util = currentGoal.CalculateUtility(selectedNPC);
            _currentGoalNameLabel.Text = currentGoal.Name;
            _currentGoalScoreLabel.Text = util.ToString("F2");
            _currentGoalUtilityBar.Value = util;
        }
        else
        {
            _currentGoalNameLabel.Text = "No Active Goal";
            _currentGoalScoreLabel.Text = "---";
            _currentGoalUtilityBar.Value = 0;
        }

        // --- Update Utilities List ---
        if (availableGoals != null)
        {
            HashSet<string> activeKeys = new HashSet<string>();
            var evaluatedGoals = availableGoals
                .Select(g => new { Goal = g, Util = g.CalculateUtility(selectedNPC) })
                .OrderByDescending(x => x.Util)
                .ToList();

            foreach (var item in evaluatedGoals)
            {
                string key = item.Goal.Name;
                activeKeys.Add(key);

                if (!_utilityRows.TryGetValue(key, out var row))
                {
                    row = new UtilityRow();
                    _utilitiesListContainer.AddChild(row);
                    _utilityRows[key] = row;
                }

                bool isActive = (currentGoal != null && item.Goal == currentGoal);
                row.Update(key, item.Util, isActive);
                
                int targetIndex = evaluatedGoals.IndexOf(item);
                if (row.GetIndex() != targetIndex)
                {
                    _utilitiesListContainer.MoveChild(row, targetIndex);
                }
            }
            
            var keysToRemove = new List<string>();
            foreach (var kvp in _utilityRows)
            {
                if (!activeKeys.Contains(kvp.Key))
                {
                    kvp.Value.QueueFree();
                    keysToRemove.Add(kvp.Key);
                }
            }
            foreach (var k in keysToRemove) _utilityRows.Remove(k);
        }

        // --- Update Plan Steps ---
        foreach (var child in _planContainer.GetChildren()) child.QueueFree();

        if (currentPlan != null && !currentPlan.IsComplete)
        {
            for (int i = 0; i < currentPlan.Steps.Count; i++)
            {
                var step = currentPlan.Steps[i];
                var stepNode = new PlanStepNode();
                
                StepState state = StepState.Pending;
                if (i < currentStepIndex) state = StepState.Completed;
                else if (i == currentStepIndex) state = StepState.Active;

                stepNode.UpdateStep(step.Name, state);
                _planContainer.AddChild(stepNode);

                // Add connector arrow if not last step
                if (i < currentPlan.Steps.Count - 1)
                {
                    var arrowLabel = new Label();
                    arrowLabel.Text = "⬇";
                    arrowLabel.HorizontalAlignment = HorizontalAlignment.Center;
                    arrowLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.3f, 0.35f));
                    arrowLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
                    _planContainer.AddChild(arrowLabel);
                }
            }
        }
        else if (currentPlan != null)
        {
             var statusLabel = new Label();
             statusLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
             
             if (currentPlan.Succeeded)
             {
                 statusLabel.Text = "✓ Plan Completed";
                 statusLabel.AddThemeColorOverride("font_color", Colors.LightGreen);
             }
             else
             {
                 statusLabel.Text = "✕ Plan Failed";
                 statusLabel.AddThemeColorOverride("font_color", Colors.Salmon);
             }
             _planContainer.AddChild(statusLabel);
        }
        else
        {
            var noPlanLabel = new Label { Text = "Waiting for plan..." };
            noPlanLabel.HorizontalAlignment = HorizontalAlignment.Center;
            noPlanLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.4f));
            noPlanLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
            _planContainer.AddChild(noPlanLabel);
        }
    }
}
