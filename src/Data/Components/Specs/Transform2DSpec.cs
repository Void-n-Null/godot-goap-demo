using Godot;

namespace Game.Data.Components.Specs;

[GlobalClass]
public partial class Transform2DSpec : ComponentSpec
{
    [Export] public Vector2 Position { get; set; }
    [Export] public float Rotation { get; set; }
    [Export] public Vector2 Scale { get; set; } = new Vector2(1, 1);

    public override IComponent CreateComponent()
    {
        return new TransformComponent2D(Position, Rotation, Scale);
    }
}


