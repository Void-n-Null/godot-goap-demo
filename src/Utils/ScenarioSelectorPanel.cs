using Godot;
using System.Linq;
using Game.Universe;

namespace Game.Utils;

/// <summary>
/// UI panel that displays a scenario selector dropdown in the top-center of the screen
/// </summary>
public partial class ScenarioSelectorPanel : SingletonNode<ScenarioSelectorPanel>
{
    private CanvasLayer _layer;
    private PanelContainer _panel;
    private OptionButton _dropdown;
    private string _currentScenario;

    public override void _Ready()
    {
        base._Ready();

        // Create UI layer
        _layer = new CanvasLayer { Name = "ScenarioSelectorLayer" };
        _layer.Layer = 100; // Above other UI
        AddChild(_layer);

        // Create panel container (top-center)
        _panel = new PanelContainer { Name = "ScenarioSelectorPanel" };
        _panel.MouseFilter = Control.MouseFilterEnum.Stop;

        // Style the panel with glassmorphism effect
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.1f, 0.92f),
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderColor = new Color(0.4f, 0.5f, 0.7f, 0.6f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
            ShadowColor = new Color(0, 0, 0, 0.4f),
            ShadowSize = 4,
            ShadowOffset = new Vector2(0, 2)
        };
        _panel.AddThemeStyleboxOverride("panel", style);

        _layer.AddChild(_panel);

        // Create HBox for label and dropdown
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(hbox);

        // Label
        var label = new Label { Text = "Scenario:" };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 1f));
        hbox.AddChild(label);

        // Dropdown
        _dropdown = new OptionButton();
        _dropdown.CustomMinimumSize = new Vector2(200, 0);

        // Style the dropdown
        var dropdownStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.12f, 0.15f, 0.95f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderColor = new Color(0.4f, 0.5f, 0.7f, 0.5f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };
        _dropdown.AddThemeStyleboxOverride("normal", dropdownStyle);

        var dropdownHoverStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.15f, 0.2f, 0.95f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderColor = new Color(0.5f, 0.6f, 0.8f, 0.7f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };
        _dropdown.AddThemeStyleboxOverride("hover", dropdownHoverStyle);
        _dropdown.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 1f));
        _dropdown.AddThemeFontSizeOverride("font_size", 14);

        hbox.AddChild(_dropdown);

        // Populate dropdown with scenarios
        PopulateScenarios();

        // Connect selection event
        _dropdown.ItemSelected += OnScenarioSelected;

        LM.Info("[ScenarioSelector] Panel created and ready");
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Position panel at top-center
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var panelSize = _panel.Size;

        _panel.Position = new Vector2(
            (viewportSize.X - panelSize.X) / 2, // Center horizontally
            10 // 10 pixels from top
        );
    }

    private void PopulateScenarios()
    {
        var scenarios = GameManager.DiscoverScenarios();

        int selectedIndex = 0;
        int currentIndex = 0;

        foreach (var kvp in scenarios.OrderBy(s => s.Key))
        {
            var scenarioType = kvp.Value;
            var scenario = (Universe.Scenarios.Scenario)System.Activator.CreateInstance(scenarioType);

            // Create display text with name and entities
            var displayText = $"{scenario.Name} - {scenario.Entities}";
            _dropdown.AddItem(displayText);
            _dropdown.SetItemMetadata(currentIndex, kvp.Key); // Store the scenario key

            // Check if this is the current scenario
            if (kvp.Key == GameManager.Instance.StartingScenario ||
                scenario.Name == GameManager.Instance.StartingScenario)
            {
                selectedIndex = currentIndex;
                _currentScenario = kvp.Key;
            }

            currentIndex++;
        }

        // Set the current scenario as selected
        _dropdown.Selected = selectedIndex;
    }

    private void OnScenarioSelected(long index)
    {
        var scenarioKey = _dropdown.GetItemMetadata((int)index).AsString();

        if (scenarioKey == _currentScenario)
        {
            return; // Already on this scenario
        }

        LM.Info($"[ScenarioSelector] Switching to scenario: {scenarioKey}");
        _currentScenario = scenarioKey;

        // Switch the scenario
        GameManager.Instance.SwitchScenario(scenarioKey);
    }
}
