using Godot;
using System;

namespace Game;

/// <summary>
/// A singleton class that manages the game logic.
/// </summary>
public partial class GameManager : Utils.SingletonNode2D<GameManager>
{
	/// <summary>
	/// Event that fires every physics tick. Subscribe to this to receive tick updates.
	/// </summary>
	/// <param name="callback">The callback to subscribe to</param>
	private event Action<double> Frame;
	[Export] int TickRatePerSecond = 60;
	private TimeManager _timeManager;


	public void SubscribeToTick(Action<double> callback){
		EnsureTimeManagerInitialized();
		_timeManager.Tick += callback;
	}

	public void UnsubscribeFromTick(Action<double> callback){
		EnsureTimeManagerInitialized();
		_timeManager.Tick -= callback;
	}

	public void SubscribeToFrame(Action<double> callback){
		Frame += callback;
	}

	public void UnsubscribeFromFrame(Action<double> callback){
		Frame -= callback;
	}


	private void EnsureTimeManagerInitialized()
	{
		_timeManager ??= new TimeManager(TickRatePerSecond);
	}

	public override void _Ready()
	{
		GD.Print("GameManager: Ready");
		base._Ready();
		EnsureTimeManagerInitialized();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		// Trigger the frame event
		Frame?.Invoke(delta);

		EnsureTimeManagerInitialized();
		_timeManager.Progress(delta);
	}
}
