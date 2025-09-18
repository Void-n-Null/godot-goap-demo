using Godot;
using System;
using Game.Data;

namespace Game.Universe;

/// <summary>
/// A singleton class that manages the game logic.
/// </summary>
public partial class GameManager : Utils.SingletonNode<GameManager>
{
	/// <summary>
	/// Event that fires every physics tick. Subscribe to this to receive tick updates.
	/// </summary>
	/// <param name="callback">The callback to subscribe to</param>
	private event Action<double> Frame;
	[Export] int TickRatePerSecond = 60;

	private event Action<double> Tick;
	private double _accumulatedTime = 0.0;
	private double TickInterval => 1.0 / TickRatePerSecond;


	public void SubscribeToTick(Action<double> callback){
		Tick += callback;
	}

	public void UnsubscribeFromTick(Action<double> callback){
		Tick -= callback;
	}

	public void SubscribeToFrame(Action<double> callback){
		Frame += callback;
	}

	public void UnsubscribeFromFrame(Action<double> callback){
		Frame -= callback;
	}

	public override void _Ready()
	{
		GD.Print("GameManager: Ready");
		base._Ready();

        // Test spawn: a simple visual-only entity (Girl) with a sprite
        var spawnPos = new Vector2(200, 200);
        EntityManager.Instance.Spawn(Blueprints.Girl, spawnPos);
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		// Trigger the frame event
		Frame?.Invoke(delta);

		// Tick progression
		if (TickRatePerSecond > 0)
		{
			_accumulatedTime += delta;
			while (_accumulatedTime >= TickInterval)
			{
				_accumulatedTime -= TickInterval;
				Tick?.Invoke(TickInterval);
			}
		}
	}
}
