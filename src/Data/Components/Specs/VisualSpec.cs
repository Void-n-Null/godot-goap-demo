using Godot;

namespace Game.Data.Components.Specs;

[GlobalClass]
public partial class VisualSpec : ComponentSpec
{
    [Export] public string ScenePath { get; set; }
    [Export] public string SpritePath { get; set; }

    public override IComponent CreateComponent()
    {
        var vc = new VisualComponent(ScenePath)
        {
            PendingSpritePath = SpritePath
        };
        return vc;
    }
}


