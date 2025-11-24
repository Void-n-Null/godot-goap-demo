using Godot;
using Game.Utils;

namespace Game.Utils;

public partial class PlanStepNode : PanelContainer
{
    private Label _stepNameLabel;
    private Label _arrowLabel; // To show arrow pointing down to next step
    private StyleBoxFlat _normalStyle;
    private StyleBoxFlat _activeStyle;
    private StyleBoxFlat _completedStyle;
    private StyleBoxFlat _pendingStyle;

    public PlanStepNode()
    {
        CustomMinimumSize = new Vector2(0, 55); // Slightly taller for the box
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // --- Styles ---
        _normalStyle = CreateStyle(new Color(0.2f, 0.2f, 0.25f), Colors.Transparent);
        
        // Active: Glowing Gold Border
        _activeStyle = CreateStyle(new Color(0.25f, 0.25f, 0.3f), new Color(1f, 0.8f, 0.2f));
        _activeStyle.ShadowColor = new Color(1f, 0.8f, 0.2f, 0.4f);
        _activeStyle.ShadowSize = 8;

        // Completed: Green tint
        _completedStyle = CreateStyle(new Color(0.15f, 0.3f, 0.15f), new Color(0.4f, 0.8f, 0.4f, 0.5f));

        // Pending: Dimmed
        _pendingStyle = CreateStyle(new Color(0.12f, 0.12f, 0.14f), new Color(0.3f, 0.3f, 0.3f));

        AddThemeStyleboxOverride("panel", _pendingStyle);

        // --- Layout ---
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 0);
        AddChild(vbox);

        // The Box Content
        var contentMargin = new MarginContainer();
        contentMargin.AddThemeConstantOverride("margin_left", 10);
        contentMargin.AddThemeConstantOverride("margin_right", 10);
        contentMargin.AddThemeConstantOverride("margin_top", 8);
        contentMargin.AddThemeConstantOverride("margin_bottom", 8);
        contentMargin.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(contentMargin);

        var hbox = new HBoxContainer();
        contentMargin.AddChild(hbox);

        var iconLabel = new Label(); // Placeholder for an action icon if we had one
        iconLabel.Text = "⚡"; 
        iconLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
        iconLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.3f));
        hbox.AddChild(iconLabel);

        _stepNameLabel = new Label();
        _stepNameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _stepNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _stepNameLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
        hbox.AddChild(_stepNameLabel);

        // Connecting Arrow (outside the box visually, but inside this container for now)
        // Actually, to make the arrow look like it's BETWEEN boxes, we can add it at the bottom
        // But the PanelContainer styling covers the whole node. 
        // Strategy: This node is just the BOX. The Arrow is separate or part of the VBox in parent.
        // Let's make this node just the box.
    }

    public void UpdateStep(string name, StepState state)
    {
        _stepNameLabel.Text = name;

        switch (state)
        {
            case StepState.Completed:
                AddThemeStyleboxOverride("panel", _completedStyle);
                _stepNameLabel.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.7f));
                Modulate = new Color(1, 1, 1, 0.7f);
                break;
            case StepState.Active:
                AddThemeStyleboxOverride("panel", _activeStyle);
                _stepNameLabel.AddThemeColorOverride("font_color", Colors.White);
                Modulate = Colors.White;
                break;
            case StepState.Pending:
                AddThemeStyleboxOverride("panel", _pendingStyle);
                _stepNameLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
                Modulate = new Color(1, 1, 1, 0.8f);
                break;
        }
    }

    private StyleBoxFlat CreateStyle(Color bg, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        };
    }
}

public enum StepState
{
    Pending,
    Active,
    Completed
}

