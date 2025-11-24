using Godot;
using Game.Data;
using Game.Data.Components;
using System.Linq;
using System.Collections.Generic;

namespace Game.Utils;

public partial class NPCInspectorInventoryTab : VBoxContainer
{
    private GridContainer _inventoryGrid;
    private Label _emptyLabel;
    
    // Cache for inventory slots to avoid rebuilding every frame
    // Key: Resource Name (TargetType.ToString())
    private Dictionary<string, InventorySlot> _slots = new();

    private partial class InventorySlot : PanelContainer
    {
        private Label _iconLabel;
        private Label _countLabel;
        private Label _nameLabel;

        public InventorySlot()
        {
            CustomMinimumSize = new Vector2(0, 50);
            
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.18f, 1f),
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                BorderColor = new Color(0.3f, 0.3f, 0.35f),
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };
            AddThemeStyleboxOverride("panel", style);

            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 10);
            AddChild(hbox);

            // Icon background/frame could be added here if we had textures
            _iconLabel = new Label();
            _iconLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeIcon);
            _iconLabel.CustomMinimumSize = new Vector2(30, 0);
            _iconLabel.HorizontalAlignment = HorizontalAlignment.Center;
            hbox.AddChild(_iconLabel);

            var infoVBox = new VBoxContainer();
            infoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            infoVBox.Alignment = BoxContainer.AlignmentMode.Center;
            hbox.AddChild(infoVBox);

            _nameLabel = new Label();
            _nameLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
            _nameLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            infoVBox.AddChild(_nameLabel);

            _countLabel = new Label();
            _countLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeSmall);
            _countLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            infoVBox.AddChild(_countLabel);
        }

        public void Update(string name, int count, string icon)
        {
            _nameLabel.Text = name;
            _countLabel.Text = $"Count: {count}";
            _iconLabel.Text = icon;
        }
    }

    public override void _Ready()
    {
        Name = "Inventory";
        AddThemeConstantOverride("separation", 10);
        AddThemeConstantOverride("margin_top", 15);
        AddThemeConstantOverride("margin_left", 10);
        AddThemeConstantOverride("margin_right", 10);
        AddThemeConstantOverride("margin_bottom", 15);

        // Title / Header
        var headerLabel = new Label();
        headerLabel.Text = "Inventory Storage";
        headerLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeHeader);
        headerLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));
        AddChild(headerLabel);

        AddChild(new HSeparator());

        // Grid for items
        _inventoryGrid = new GridContainer();
        _inventoryGrid.Columns = 1; // List view essentially, but expandable to grid if needed
        _inventoryGrid.AddThemeConstantOverride("v_separation", 8);
        _inventoryGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_inventoryGrid);

        // Empty State Label
        _emptyLabel = new Label();
        _emptyLabel.Text = "Inventory is empty";
        _emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        _emptyLabel.AddThemeFontSizeOverride("font_size", NPCInspectorPanel.FontSizeNormal);
        _emptyLabel.CustomMinimumSize = new Vector2(0, 100);
        _emptyLabel.VerticalAlignment = VerticalAlignment.Center;
        _emptyLabel.Visible = false;
        AddChild(_emptyLabel);
    }

    public void UpdateTab(Entity selectedNPC)
    {
        if (selectedNPC == null) return;

        if (selectedNPC.TryGetComponent<NPCData>(out var npcData))
        {
            var currentResources = npcData.Resources;

            if (currentResources.Count == 0)
            {
                _inventoryGrid.Visible = false;
                _emptyLabel.Visible = true;
                return;
            }

            _inventoryGrid.Visible = true;
            _emptyLabel.Visible = false;

            // Track which slots are active this frame to hide unused ones
            HashSet<string> activeKeys = new HashSet<string>();

            foreach (var kvp in currentResources.OrderBy(x => x.Key.ToString()))
            {
                string key = kvp.Key.ToString();
                activeKeys.Add(key);

                if (!_slots.TryGetValue(key, out var slot))
                {
                    slot = new InventorySlot();
                    _inventoryGrid.AddChild(slot);
                    _slots[key] = slot;
                }

                slot.Visible = true;
                slot.Update(key, kvp.Value, GetResourceIcon(kvp.Key));
            }

            // Hide slots for items that are no longer in inventory
            foreach (var kvp in _slots)
            {
                if (!activeKeys.Contains(kvp.Key))
                {
                    kvp.Value.Visible = false;
                }
            }
        }
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
