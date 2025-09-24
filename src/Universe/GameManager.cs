using Godot;
using System;
using System.Collections.Generic;
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
	[Export] int MaxTicksPerFrame = 4; // max slices processed per frame when catching up
	[Export] double MaxAccumulatedTimeSeconds = 0.25; // cap backlog to ~15 ticks at 60 Hz
	[Export] int MaxFramesPerTick = 3; // Preferred number of frames to distribute a tick across when possible
	[Export(PropertyHint.Range, "0.01,1,0.01")] float FrameTimeSmoothing = 0.1f;

	private event Action<double> Tick;
	private double _accumulatedTime = 0.0;
	private double TickInterval => 1.0 / TickRatePerSecond;
	private double _smoothedFrameTime = 1.0 / 60.0;
	public double SmoothedFps { get; private set; } = 60.0;

	private sealed class TickWorkItem
	{
		public double Delta { get; }
		public int TotalSlices { get; }
		public int CompletedSlices { get; set; }

		public TickWorkItem(double delta, int totalSlices)
		{
			Delta = delta;
			TotalSlices = Math.Max(1, totalSlices);
		}
	}

	private readonly Queue<TickWorkItem> _pendingTicks = new();

	/// <summary>
	/// The total number of slices scheduled for the tick currently being processed.
	/// Consumers like EntityManager can inspect this to determine how many entities to update.
	/// Defaults to 1 outside of slice processing.
	/// </summary>
	public int CurrentTickSliceCount { get; private set; } = 1;

	/// <summary>
	/// The zero-based index of the slice currently being processed.
	/// </summary>
	public int CurrentTickSliceIndex { get; private set; } = 0;


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

	public void OnTick(double delta){

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
			AddChild(new EntityDebugTooltip());
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
		// for (int i = 0; i < 3000; i++)
		//SpawnEntity.Now(NPC.Wanderer)
		for (int i = 0; i < 200; i++)
			SpawnEntity.Now(NPC.Intelligent, Utils.Random.NextVector2(-500, 500));
		for (int i = 0; i < 1200; i++) 
			SpawnEntity.Now(Food.RawBeef, Utils.Random.NextVector2(-5000, 5000));
		for (int i = 0; i < 6000; i++)
			SpawnEntity.Now(Furniture.Bed, Utils.Random.NextVector2(-10000, 10000));
		
		// // Schedule a few beds to test furniture blueprints after 120 ticks
		// for (int i = 0; i < 400; i++)
		// 	SpawnEntity.AfterTickDelay(Furniture.Bed, Utils.Random.NextVector2(-1000, 1000), 60);
		
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		UpdateSmoothedFrameTime(delta);
		double fpsEstimate = SmoothedFps;

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
			while (_accumulatedTime >= TickInterval)
			{
				_accumulatedTime -= TickInterval;
				int slicesForTick = DetermineSlicesForTick(fpsEstimate);
				_pendingTicks.Enqueue(new TickWorkItem(TickInterval, slicesForTick));
			}
		}

		ProcessTickSlices();
	}

	private void UpdateSmoothedFrameTime(double delta)
	{
		float smoothing = Mathf.Clamp(FrameTimeSmoothing, 0.01f, 1f);
		_smoothedFrameTime = _smoothedFrameTime * (1.0 - smoothing) + delta * smoothing;
		if (_smoothedFrameTime <= 0.000001)
		{
			SmoothedFps = TickRatePerSecond;
		}
		else
		{
			SmoothedFps = 1.0 / _smoothedFrameTime;
		}
	}

	private int DetermineSlicesForTick(double fpsEstimate)
	{
		if (TickRatePerSecond <= 0)
			return 1;

		if (fpsEstimate <= TickRatePerSecond * 1.05)
			return 1;

		var ratio = fpsEstimate / TickRatePerSecond;
		int desiredSlices = Mathf.Clamp(Mathf.FloorToInt((float)Math.Floor(ratio)), 1, MaxFramesPerTick);
		return Math.Max(1, desiredSlices);
	}

	private void ProcessTickSlices()
	{
		if (_pendingTicks.Count == 0)
		{
			CurrentTickSliceCount = 1;
			CurrentTickSliceIndex = 0;
			return;
		}

		int sliceBudgetThisFrame = _pendingTicks.Count > 1 ? Math.Max(1, MaxTicksPerFrame) : 1;
		int processedThisFrame = 0;

		while (_pendingTicks.Count > 0 && processedThisFrame < sliceBudgetThisFrame)
		{
			var work = _pendingTicks.Peek();
			CurrentTickSliceCount = work.TotalSlices;
			CurrentTickSliceIndex = work.CompletedSlices;

			Tick?.Invoke(work.Delta);
			OnTick(work.Delta);

			work.CompletedSlices++;
			processedThisFrame++;

			if (work.CompletedSlices >= work.TotalSlices)
			{
				_pendingTicks.Dequeue();
			}
		}

		if (_pendingTicks.Count == 0)
		{
			CurrentTickSliceCount = 1;
			CurrentTickSliceIndex = 0;
		}
	}
}
