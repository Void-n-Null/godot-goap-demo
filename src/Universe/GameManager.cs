using Godot;
using System;
using Game.Utils;
using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;

namespace Game.Universe;

/// <summary>
/// A singleton class that manages the game logic.
/// </summary>
public partial class GameManager : SingletonNode<GameManager>
{
	[Export] public bool DebugMode = false;
	/// <summary>
	/// Event that fires every physics tick. Subscribe to this to receive tick updates.
	/// </summary>
	/// <param name="callback">The callback to subscribe to</param>
	private event Action<double> Frame;
	[Export] int TickRatePerSecond = 60;
	[Export] int MaxTicksPerFrame = 4; // prevent spiral of death
	[Export] double MaxAccumulatedTimeSeconds = 0.25; // cap backlog to ~15 ticks at 60 Hz

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
		Utils.Random.Initialize();
		GD.Print("GameManager: Ready");
		base._Ready();

		if (DebugMode)
		{
			GD.Print("GameManager: DebugMode enabled");
			AddChild(new StatsOverlay());
		}

		// Ensure the TaskScheduler exists in the scene
		if (!TaskScheduler.HasInstance)
		{
			AddChild(new TaskScheduler());
		}


		// Schedule spawn: girls after 60 ticks
		// for (int i = 0; i < 100; i++)
		// 	SpawnEntity.Now(NPC.Follower, Utils.Random.NextVector2(-5000, 5000));
		// for (int i = 0; i < 10; i++)
		// 	SpawnEntity.Now(NPC.Wanderer, Utils.Random.NextVector2(-5000, 5000));
		for (int i = 0; i < 2000; i++)
			SpawnEntity.Now(NPC.Wanderer);
	
		// // Schedule a few beds to test furniture blueprints after 120 ticks
		// for (int i = 0; i < 400; i++)
		// 	SpawnEntity.AfterTickDelay(Furniture.Bed, Utils.Random.NextVector2(-1000, 1000), 60);
		
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		// Cache mouse position once per frame for MovementComponent and others
		if (EntityManager.Instance != null && EntityManager.Instance.ViewRoot is Node2D root2D)
		{
			ViewContext.CachedMouseGlobalPosition = root2D.GetGlobalMousePosition();
		}
		else
		{
			ViewContext.CachedMouseGlobalPosition = null;
		}

		// Trigger the frame event
		Frame?.Invoke(delta);

		// Tick progression with spiral-of-death protection
		if (TickRatePerSecond > 0)
		{
			_accumulatedTime = Math.Min(_accumulatedTime + delta, MaxAccumulatedTimeSeconds);
			int ticksThisFrame = 0;
			while (_accumulatedTime >= TickInterval && ticksThisFrame < MaxTicksPerFrame)
			{
				_accumulatedTime -= TickInterval;
				Tick?.Invoke(TickInterval);
				ticksThisFrame++;
			}
		}
	}
}
