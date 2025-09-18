using Godot;

namespace Game.Data.Components.Specs;

[GlobalClass]
public partial class MovementSpec : ComponentSpec
{
    [Export] public float MaxSpeed { get; set; } = 500f;
    [Export] public float Friction { get; set; } = 0.9f;

    public override IComponent CreateComponent()
    {
        return new MovementComponent(MaxSpeed, Friction);
    }
}


