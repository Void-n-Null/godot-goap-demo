using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Utils;
using Game.Data;
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
	[Export] int MaxFramesPerTick = 10; // Preferred number of frames to distribute a tick across when possible
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

	// Scale Profiling System
	private enum ProfilingStage
	{
		Inactive,
		Stage1_ProgressiveSpawning,
		Stage2_BatchSpawning,
		Complete
	}

	private class ProfilingData
	{
		public double Timestamp { get; set; }
		public int IntelligentCount { get; set; }
		public int TreeCount { get; set; }
		public int BeefCount { get; set; }
		public int TotalEntities { get; set; }
		public double Fps { get; set; }
		public string Stage { get; set; }
		public int TickNumber { get; set; }
		public bool HitCeiling { get; set; }
	}

	private ProfilingStage _currentProfilingStage = ProfilingStage.Inactive;
	private List<ProfilingData> _profilingDataPoints = new();
	private int _profilingTickCounter = 0;
	private int _stage1BatchIndex = 0;
	private int[] _stage1IntelligentCounts = {
		10, 20, 30, 40, 50, 60, 70, 80, 90, 100,  // 10-100 in increments of 10
		110, 120, 130, 140, 150, 160, 170, 180, 190, 200,  // 100-200 in increments of 10
		220, 240, 260, 280, 300,  // 200-300 in increments of 20
		320, 340, 360, 380, 400,  // 300-400 in increments of 20
		420, 440, 460, 480, 500,  // 400-500 in increments of 20
		520, 540, 560, 580, 600,  // 500-600 in increments of 20
		620, 640, 660, 680, 700,  // 600-700 in increments of 20
		710, 720, 730, 740, 750, 760, 770, 780, 790, 800,  // 700-800 in increments of 10 (critical zone)
		820, 840, 860, 880, 900,  // 800-900 in increments of 20
		920, 940, 960, 980, 1000,  // 900-1000 in increments of 20
		1050, 1100, 1150, 1200,  // 1000-1200 in increments of 50
		1250, 1300, 1350, 1400, 1450, 1500,  // 1200-1500 in increments of 50
		1600, 1700, 1800, 1900, 2000  // 1500-2000 in increments of 100
	};
	private const int STAGE1_TICKS_PER_SPAWN = 360; // Wait 360 ticks (6 seconds at 60Hz) between spawns for full stabilization
	private const int STAGE1_INTELLIGENT_PER_SPAWN = 1;
	private const int STAGE1_MAX_INTELLIGENT = 2100; // ~14,700 total entities (under 15k limit)
	private const int STAGE2_STABILIZATION_TICKS = 360; // Wait 360 ticks (6 seconds) for batch stabilization
	private double _timeSinceProfilingStart = 0;

	// Performance ceiling detection
	private const double CEILING_FPS_THRESHOLD = 7.0;
	private const int CEILING_TICK_THRESHOLD = 60;
	private const int CEILING_CONFIRMATION_TICKS = 60; // 1 second to confirm ceiling before exiting (reduced from 2s)
	private int _lowFpsTickCounter = 0;
	private bool _performanceCeilingHit = false;
	private int _ticksSinceCeilingHit = 0;


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
		UpdateScaleProfiling(delta);
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


		// for (int i = 0; i < 1000; i++)
		// 	SpawnEntity.Now(NPC.Wanderer, Utils.Random.NextVector2(-5000, 5000));
		// for (int i = 0; i < 10; i++)
		// 	SpawnEntity.Now(NPC.Wanderer, Utils.Random.NextVector2(-5000, 5000));
		// for (int i = 0; i < 3000; i++)
		//SpawnEntity.Now(NPC.Wanderer)
		// for (int i = 0; i < 120; i++)
		// 	SpawnEntity.Now(NPC.Intelligent, Utils.Random.NextVector2(-500, 500));
		// for (int i = 0; i < 700; i++) 
		// 	SpawnEntity.Now(Food.RawBeef, Utils.Random.NextVector2(-5000, 5000));
		// for (int i = 0; i < 6000; i++)
		// 	SpawnEntity.Now(Furniture.Bed, Utils.Random.NextVector2(-10000, 10000));

		// Spawn trees for wood gathering
		for (int i = 0; i < 1500; i++)
			SpawnEntity.Now(Nature.SimpleTree, Utils.Random.NextVector2(-15000, 15000));
		
		// Spawn food for eating
		for (int i = 0; i < 1500; i++)
			SpawnEntity.Now(Food.RawBeef, Utils.Random.NextVector2(-15000, 15000));
		
		// Spawn NPCs with randomized hunger
		for (int i = 0; i < 500; i++)
			SpawnEntity.Now(NPC.Intelligent, Utils.Random.NextVector2(-3000, 3000));
		// for (int i = 0; i < 15000; i++)
		// 	SpawnEntity.Now(Items.Stick, Utils.Random.NextVector2(-10000, 10000));
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

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.G)
			{
				StartScaleProfiling();
			}
		}
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

	// ========== Scale Profiling Methods ==========

	private void StartScaleProfiling()
	{
		if (_currentProfilingStage != ProfilingStage.Inactive)
		{
			GD.Print("[Scale Profiling] Scale profiling already in progress!");
			return;
		}

		GD.Print("[Scale Profiling] === Starting Scale Profiling ===");

		// Delete all entities FIRST before starting profiling
		DeleteAllEntities();

		// Reset all profiling state AFTER deletion to avoid recording old entities
		_profilingDataPoints.Clear();
		_profilingTickCounter = 0;
		_stage1BatchIndex = 0;
		_timeSinceProfilingStart = 0;
		_lowFpsTickCounter = 0;
		_performanceCeilingHit = false;
		_ticksSinceCeilingHit = 0;

		// NOW activate profiling after everything is clean
		_currentProfilingStage = ProfilingStage.Stage1_ProgressiveSpawning;

		int remainingEntities = EntityManager.Instance?.GetEntities().Count ?? 0;
		GD.Print($"[Scale Profiling] All entities deleted. Remaining: {remainingEntities}");
		GD.Print("[Scale Profiling] Starting Stage 1: Progressive Spawning");
	}

	private void DeleteAllEntities()
	{
		if (EntityManager.Instance == null)
			return;

		var allEntities = EntityManager.Instance.GetEntities()
			.OfType<Entity>()
			.ToList();

		int count = allEntities.Count;

		foreach (var entity in allEntities)
		{
			// Destroy components first
			entity.Destroy();
			// Then unregister from EntityManager
			EntityManager.Instance.UnregisterEntity(entity);
		}

		int remaining = EntityManager.Instance.GetEntities().Count;
		GD.Print($"[Scale Profiling] Deleted {count} entities, {remaining} remaining");
	}

	private void UpdateScaleProfiling(double delta)
	{
		if (_currentProfilingStage == ProfilingStage.Inactive)
			return;

		_timeSinceProfilingStart += delta;
		_profilingTickCounter++;

		// Use Godot's accurate FPS counter instead of custom smoothing
		double currentFps = Engine.GetFramesPerSecond();

		// More frequent logging when performance is bad
		if (currentFps < 30.0 && _profilingTickCounter % 10 == 0)
		{
			int totalEntities = EntityManager.Instance?.GetEntities().Count ?? 0;
			GD.Print($"[Scale Profiling] ⚠️ Low FPS detected: {currentFps:F1} | Entities: {totalEntities} | Tick: {_profilingTickCounter}");
		}

		// Check for performance ceiling
		if (currentFps < CEILING_FPS_THRESHOLD)
		{
			_lowFpsTickCounter++;
			if (_lowFpsTickCounter >= CEILING_TICK_THRESHOLD && !_performanceCeilingHit)
			{
				_performanceCeilingHit = true;
				int totalEntities = EntityManager.Instance?.GetEntities().Count ?? 0;
				GD.Print($"[Scale Profiling] ⚠️ Performance ceiling hit! FPS: {currentFps:F1} (below {CEILING_FPS_THRESHOLD} for {CEILING_TICK_THRESHOLD} ticks)");
				GD.Print($"[Scale Profiling] Entities at ceiling: {totalEntities}");
				GD.Print($"[Scale Profiling] Collecting data for {CEILING_CONFIRMATION_TICKS} more ticks before completing profiling...");
			}

			// Print every 30 ticks while under ceiling to confirm ticks are still happening
			if (_performanceCeilingHit && _ticksSinceCeilingHit % 30 == 0)
			{
				GD.Print($"[Scale Profiling] Ceiling confirmation: {_ticksSinceCeilingHit}/{CEILING_CONFIRMATION_TICKS} ticks | FPS: {currentFps:F1}");
			}
		}
		else
		{
			_lowFpsTickCounter = 0;
		}

		// If ceiling hit, increment confirmation counter
		if (_performanceCeilingHit)
		{
			_ticksSinceCeilingHit++;
			if (_ticksSinceCeilingHit >= CEILING_CONFIRMATION_TICKS)
			{
				// In Stage 1, move to Stage 2. In Stage 2, exit.
				if (_currentProfilingStage == ProfilingStage.Stage1_ProgressiveSpawning)
				{
					GD.Print($"[Scale Profiling] Stage 1 ceiling confirmed. FPS: {currentFps:F1}. Moving to Stage 2.");
					RecordProfilingData("Stage1_ProgressiveSpawning");
					DeleteAllEntities();
					_currentProfilingStage = ProfilingStage.Stage2_BatchSpawning;
					_profilingTickCounter = 0;
					_performanceCeilingHit = false;
					_ticksSinceCeilingHit = 0;
					_lowFpsTickCounter = 0;
				}
				else if (_currentProfilingStage == ProfilingStage.Stage2_BatchSpawning)
				{
					GD.Print($"[Scale Profiling] Stage 2 ceiling confirmed. FPS: {currentFps:F1}. Ending profiling.");
					_currentProfilingStage = ProfilingStage.Complete;
				}
			}
		}

		switch (_currentProfilingStage)
		{
			case ProfilingStage.Stage1_ProgressiveSpawning:
				UpdateStage1Progressive();
				break;
			case ProfilingStage.Stage2_BatchSpawning:
				UpdateStage2Batch();
				break;
			case ProfilingStage.Complete:
				ExportProfilingDataToCsv();
				_currentProfilingStage = ProfilingStage.Inactive;
				break;
		}
	}

	private void UpdateStage1Progressive()
	{
		// Don't spawn more entities if ceiling hit
		if (!_performanceCeilingHit && _profilingTickCounter % STAGE1_TICKS_PER_SPAWN == 0)
		{
			int currentIntelligent = GetEntityCountByType(NPC.Intelligent);

			if (currentIntelligent >= STAGE1_MAX_INTELLIGENT)
			{
				double currentFps = Engine.GetFramesPerSecond();
				GD.Print($"[Scale Profiling] Stage 1 complete. FPS: {currentFps:F1}, Entities: {EntityManager.Instance?.GetEntities().Count ?? 0}");
				GD.Print("[Scale Profiling] Starting Stage 2: Batch Spawning");
				RecordProfilingData("Stage1_ProgressiveSpawning");
				DeleteAllEntities();
				_currentProfilingStage = ProfilingStage.Stage2_BatchSpawning;
				_profilingTickCounter = 0;
				_performanceCeilingHit = false;
				_ticksSinceCeilingHit = 0;
				_lowFpsTickCounter = 0;
				return;
			}

			// Spawn 1 intelligent + 3 trees + 3 beef
			for (int i = 0; i < STAGE1_INTELLIGENT_PER_SPAWN; i++)
				SpawnEntity.Now(NPC.Intelligent, Utils.Random.NextVector2(-5000, 5000));
			for (int i = 0; i < 3; i++)
				SpawnEntity.Now(Nature.SimpleTree, Utils.Random.NextVector2(-5000, 5000));
			for (int i = 0; i < 3; i++)
				SpawnEntity.Now(Food.RawBeef, Utils.Random.NextVector2(-5000, 5000));

			double spawnFps = Engine.GetFramesPerSecond();
			int totalEntities = EntityManager.Instance?.GetEntities().Count ?? 0;
			GD.Print($"[Scale Profiling] Stage 1 spawned: {currentIntelligent + 1} intelligent, {totalEntities + 7} total entities | FPS: {spawnFps:F1}");
		}

		// Record data every tick during progressive spawning for detailed tracking
		RecordProfilingData("Stage1_ProgressiveSpawning");
	}

	private void UpdateStage2Batch()
	{
		// Don't advance to next batch if ceiling hit
		if (!_performanceCeilingHit && _profilingTickCounter % STAGE2_STABILIZATION_TICKS == 0 && _profilingTickCounter > 0)
		{
			// Record data point before spawning next batch
			RecordProfilingData("Stage2_BatchSpawning");

			// Move to next batch
			_stage1BatchIndex++;
			double currentFps = Engine.GetFramesPerSecond();

			if (_stage1BatchIndex >= _stage1IntelligentCounts.Length)
			{
				// Stage 2 complete
				int totalEntities = EntityManager.Instance?.GetEntities().Count ?? 0;
				GD.Print($"[Scale Profiling] Stage 2 complete. FPS: {currentFps:F1}, Total entities: {totalEntities}");
				GD.Print("[Scale Profiling] Finalizing profiling data.");
				_currentProfilingStage = ProfilingStage.Complete;
				return;
			}

			// Spawn next batch
			int intelligentCount = _stage1IntelligentCounts[_stage1BatchIndex];
			int resourceCount = intelligentCount * 3;

			GD.Print($"[Scale Profiling] Stage 2: Spawning {intelligentCount} intelligent NPCs, {resourceCount} trees, {resourceCount} beef | FPS: {currentFps:F1}");

			for (int i = 0; i < intelligentCount; i++)
				SpawnEntity.Now(NPC.Intelligent, Utils.Random.NextVector2(-5000, 5000));
			for (int i = 0; i < resourceCount; i++)
				SpawnEntity.Now(Nature.SimpleTree, Utils.Random.NextVector2(-5000, 5000));
			for (int i = 0; i < resourceCount; i++)
				SpawnEntity.Now(Food.RawBeef, Utils.Random.NextVector2(-5000, 5000));
		}
		else if (_profilingTickCounter == 1)
		{
			// Initial spawn
			int intelligentCount = _stage1IntelligentCounts[_stage1BatchIndex];
			int resourceCount = intelligentCount * 3;
			double currentFps = Engine.GetFramesPerSecond();

			GD.Print($"[Scale Profiling] Stage 2: Initial spawn - {intelligentCount} intelligent NPCs, {resourceCount} trees, {resourceCount} beef | FPS: {currentFps:F1}");

			for (int i = 0; i < intelligentCount; i++)
				SpawnEntity.Now(NPC.Intelligent, Utils.Random.NextVector2(-5000, 5000));
			for (int i = 0; i < resourceCount; i++)
				SpawnEntity.Now(Nature.SimpleTree, Utils.Random.NextVector2(-5000, 5000));
			for (int i = 0; i < resourceCount; i++)
				SpawnEntity.Now(Food.RawBeef, Utils.Random.NextVector2(-5000, 5000));
		}
		else if (_performanceCeilingHit)
		{
			// Just record data while waiting for confirmation period to finish
			RecordProfilingData("Stage2_BatchSpawning");
		}
	}

	private int GetEntityCountByType(EntityBlueprint blueprint)
	{
		if (EntityManager.Instance == null)
			return 0;

		return EntityManager.Instance.GetEntities()
			.OfType<Entity>()
			.Count(e => e.Blueprint == blueprint);
	}

	private void RecordProfilingData(string stage)
	{
		if (EntityManager.Instance == null)
			return;

		int intelligentCount = GetEntityCountByType(NPC.Intelligent);
		int treeCount = GetEntityCountByType(Nature.SimpleTree);
		int beefCount = GetEntityCountByType(Food.RawBeef);
		int totalEntities = EntityManager.Instance.GetEntities().Count;
		double currentFps = Engine.GetFramesPerSecond();

		var dataPoint = new ProfilingData
		{
			Timestamp = _timeSinceProfilingStart,
			IntelligentCount = intelligentCount,
			TreeCount = treeCount,
			BeefCount = beefCount,
			TotalEntities = totalEntities,
			Fps = currentFps,
			Stage = stage,
			TickNumber = _profilingTickCounter,
			HitCeiling = _performanceCeilingHit
		};

		_profilingDataPoints.Add(dataPoint);
	}

	private void ExportProfilingDataToCsv()
	{
		var csv = new StringBuilder();
		csv.AppendLine("Timestamp,TickNumber,Stage,IntelligentCount,TreeCount,BeefCount,TotalEntities,FPS,HitCeiling");

		foreach (var data in _profilingDataPoints)
		{
			csv.AppendLine($"{data.Timestamp:F3},{data.TickNumber},{data.Stage},{data.IntelligentCount},{data.TreeCount},{data.BeefCount},{data.TotalEntities},{data.Fps:F2},{data.HitCeiling}");
		}

		string projectPath = ProjectSettings.GlobalizePath("res://");
		string filePath = System.IO.Path.Combine(projectPath, $"scale_profile_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

		try
		{
			System.IO.File.WriteAllText(filePath, csv.ToString());
			GD.Print($"[Scale Profiling] === Scale Profiling Complete ===");
			GD.Print($"[Scale Profiling] Profiling data exported to: {filePath}");
			GD.Print($"[Scale Profiling] Total data points: {_profilingDataPoints.Count}");

			// Clean up all entities when profiling is complete
			DeleteAllEntities();
			GD.Print($"[Scale Profiling] All profiling entities cleaned up.");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Scale Profiling] Failed to export profiling data: {ex.Message}");
		}
	}
}
