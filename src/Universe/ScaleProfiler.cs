using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Data;
using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;

namespace Game.Universe;

/// <summary>
/// A system for profiling game performance at scale by progressively spawning entities
/// and measuring FPS. Activated by pressing 'G' key.
/// </summary>
public partial class ScaleProfiler : Node
{
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

	public override void _Ready()
	{
		base._Ready();

		// Subscribe to GameManager's tick event
		if (GameManager.HasInstance)
		{
			GameManager.Instance.SubscribeToTick(OnTick);
		}
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		// Unsubscribe from GameManager's tick event
		if (GameManager.HasInstance)
		{
			GameManager.Instance.UnsubscribeFromTick(OnTick);
		}
	}

	/// <summary>
	/// Starts the scale profiling process. Call this to begin profiling.
	/// </summary>
	public void StartProfiling()
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

	private void OnTick(double delta)
	{
		UpdateScaleProfiling(delta);
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
