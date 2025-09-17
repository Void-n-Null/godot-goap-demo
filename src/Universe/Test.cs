using Godot;
using System;
using Game;

namespace Game.Universe;

public partial class Test : Node2D
{
    public override void _Ready()
    {
        GD.Print("Test: Ready");
        base._Ready();
        GameManager.Instance.SubscribeToTick(OnTick);
        GD.Print("Test: Subscribed to tick");
    }

    private void OnTick(double delta)
    {
        GD.Print("Test: Tick");
    }
}