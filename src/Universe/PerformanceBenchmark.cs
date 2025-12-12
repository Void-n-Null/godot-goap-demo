using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Data;
using Game.Data.Blueprints;
using Game.Data.Components;
using Game.Data.GOAP;
using Game.Utils;
using StdRandom = System.Random;

namespace Game.Universe;

/// <summary>
/// Purpose-built benchmarking harness for producing headline graphs (ECS throughput,
/// QuadTree allocation profile, GOAP planning latency). Press 'B' in-game to run.
/// </summary>
public partial class PerformanceBenchmark : Node
{
	private readonly struct SleepScenario
	{
		public string Name { get; }
		public Action<State> Configure { get; }

		public SleepScenario(string name, Action<State> configure)
		{
			Name = name;
			Configure = configure;
		}
	}

	private static readonly int[] EcsEntityTargets =
	{
		1_000, 5_000, 10_000, 20_000, 40_000, 60_000, 80_000, 100_000, 125_000, 150_000,
		175_000, 200_000, 225_000, 250_000
	};

	private static readonly int[] QuadTreeQueryBatches = { 500, 1_000, 2_000, 4_000 };

	private static readonly int[] ParallelAgentBatches = { 50, 100, 200, 500, 1_000, 1_500, 2_000, 2_500, 3_000, 3_500, 4_000, 4_500, 5_000, 5_500, 6_000, 6_500, 7_000, 7_500, 8_000, 8_500, 9_000, 9_500, 10_000 };

	private static readonly SleepScenario[] SleepScenarios =
	{
		new("BedNearby", state =>
		{
			state.Set(FactKeys.WorldHas(Tags.Bed), true);
			state.Set(FactKeys.WorldCount(Tags.Bed), 1);
			state.Set("IsSleepy", true);
			// Need to walk to an existing bed.
		}),
		new("InventoryHasSticks", state =>
		{
			state.Set(FactKeys.AgentHas(Tags.Stick), true);
			state.Set(FactKeys.AgentCount(Tags.Stick), 4);
			state.Set("IsSleepy", true);
		}),
		new("GatherDroppedSticks", state =>
		{
			state.Set(FactKeys.WorldHas(Tags.Stick), true);
			state.Set(FactKeys.WorldCount(Tags.Stick), 8);
			state.Set("IsSleepy", true);
		}),
		new("FullPipelineFromTrees", state =>
		{
			state.Set(FactKeys.WorldHas(Tags.Tree), true);
			state.Set(FactKeys.WorldCount(Tags.Tree), 40);
			state.Set("IsSleepy", true);
		})
	};

	private enum BenchmarkState
	{
		Idle,
		Running,
		Complete
	}

	private BenchmarkState _state = BenchmarkState.Idle;
	private readonly List<string> _results = new();
	private readonly List<Entity> _benchmarkEntities = new();

	public override void _Ready()
	{
		GD.Print("[PerformanceBenchmark] Ready. Press 'B' to run architectural benchmarks.");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Keycode: Key.B })
		{
			StartBenchmark();
		}
	}

	private async void StartBenchmark()
	{
		if (_state != BenchmarkState.Idle)
		{
			GD.Print("[PerformanceBenchmark] Benchmark already running or completed.");
			return;
		}

		if (!EntityManager.HasInstance)
		{
			GD.PrintErr("[PerformanceBenchmark] EntityManager is not ready. Cannot run benchmarks.");
			return;
		}

		_state = BenchmarkState.Running;
		_results.Clear();
		_benchmarkEntities.Clear();

		GD.Print("\n[ Benchmark ] === STARTING ARCHITECTURAL BENCHMARK SUITE ===");

		await CleanupWorld();
		await RunEcsThroughputTest();
		await RunQuadTreeQueryTest();
		await RunGoapPlannerTest();
		ExportResults();

		GD.Print("[ Benchmark ] === BENCHMARK COMPLETE ===");
		_state = BenchmarkState.Complete;
	}

	private async Task CleanupWorld()
	{
		var manager = EntityManager.Instance;

		var existing = new List<IUpdatableEntity>(manager.GetEntities());
		GD.Print($"[PerformanceBenchmark] Clearing {existing.Count} existing entities.");

		foreach (var entity in existing)
		{
			manager.UnregisterEntity(entity);
			if (entity is Entity concrete)
			{
				concrete.Destroy();
			}
		}

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task RunEcsThroughputTest()
	{
		GD.Print("\n[Test 1] ECS Throughput vs. Entity Count");
		_results.Add("Section,ECS_Count,ECS_UpdateMs");

		foreach (var targetCount in EcsEntityTargets)
		{
			int needed = targetCount - _benchmarkEntities.Count;
			if (needed > 0)
			{
				SpawnBenchmarkEntities(needed);
			}

			// Let components settle (more frames for larger entity counts)
			int settleFrames = targetCount >= 100_000 ? 15 : 5;
			for (int i = 0; i < settleFrames; i++)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			const int samples = 60;
			double totalUpdateMs = 0;

			for (int i = 0; i < samples; i++)
			{
				ulong startUsec = Time.GetTicksUsec();
				for (int j = 0; j < _benchmarkEntities.Count; j++)
				{
					_benchmarkEntities[j].Update(1.0 / 60.0);
				}

				ulong endUsec = Time.GetTicksUsec();
				totalUpdateMs += (endUsec - startUsec) / 1000.0;

				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			double avgUpdate = totalUpdateMs / samples;

			GD.Print($"Count {targetCount:N0} | Update {avgUpdate:F2} ms");
			_results.Add($"ECS,{targetCount},{avgUpdate:F2}");
		}
	}

	private async Task RunQuadTreeQueryTest()
	{
		GD.Print("\n[Test 2] QuadTree Query Cost & Allocations");
		_results.Add("Section,Queries,QueryTimeMs,AllocatedBytes,AvgTestedEntities");

		foreach (var batch in QuadTreeQueryBatches)
		{
			long gcBefore = GC.GetTotalAllocatedBytes();
			ulong startUsec = Time.GetTicksUsec();
			long totalTested = 0;

			for (int i = 0; i < batch; i++)
			{
				var center = Game.Utils.Random.NextVector2(-5_000, 5_000);
				EntityManager.Instance.SpatialPartition.QueryCircleWithCounts(
					center,
					500f,
					predicate: null,
					maxResults: 64,
					out int tested);
				totalTested += tested;
			}

			ulong endUsec = Time.GetTicksUsec();
			long allocDelta = GC.GetTotalAllocatedBytes() - gcBefore;
			double elapsedMs = (endUsec - startUsec) / 1000.0;
			double avgTested = batch > 0 ? (double)totalTested / batch : 0;

			GD.Print($"Queries {batch} | Time {elapsedMs:F2} ms | Alloc {allocDelta} bytes | Avg tested {avgTested:F1}");
			_results.Add($"QuadTree,{batch},{elapsedMs:F2},{allocDelta},{avgTested:F1}");

			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private async Task RunGoapPlannerTest()
	{
		GD.Print("\n[Test 3] GOAP Planning Latency");
		_results.Add("Section,Plans,TotalMs,MsPerPlan");

		var initialState = new State();
		initialState.Set("has_wood", false);
		initialState.Set("has_axe", false);
		initialState.Set("at_forest", false);
		initialState.Set("energy", 100);

		var goalState = new State();
		goalState.Set("has_campfire", true);

		// Warmup: stabilize JIT and fill gen0
		for (int i = 0; i < 50; i++)
		{
			AdvancedGoalPlanner.ForwardPlan(initialState, goalState);
		}

		// Stabilize GC before timing
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Thread.Sleep(50);

		const int planCount = 500;

		ulong startSerial = Time.GetTicksUsec();
		for (int i = 0; i < planCount; i++)
		{
			AdvancedGoalPlanner.ForwardPlan(initialState, goalState);
		}
		double serialMs = (Time.GetTicksUsec() - startSerial) / 1000.0;
		double serialPerPlan = serialMs / planCount;

		GD.Print($"Serial {planCount} plans -> {serialMs:F2} ms total ({serialPerPlan:F4} ms/plan)");
		_results.Add($"GOAP_Serial,{planCount},{serialMs:F2},{serialPerPlan:F4}");

		// Stabilize GC before parallel test
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Thread.Sleep(50);

		// Warm ThreadPool workers before timing
		Parallel.For(0, System.Environment.ProcessorCount * 2, _ => Thread.SpinWait(1000));

		ulong startParallel = Time.GetTicksUsec();
		Parallel.For(0, planCount, _ =>
		{
			AdvancedGoalPlanner.ForwardPlan(initialState, goalState);
		});
		double parallelMs = (Time.GetTicksUsec() - startParallel) / 1000.0;
		double parallelPerPlan = parallelMs / planCount;

		GD.Print($"Parallel {planCount} plans -> {parallelMs:F2} ms total ({parallelPerPlan:F4} ms/plan)");
		_results.Add($"GOAP_Parallel,{planCount},{parallelMs:F2},{parallelPerPlan:F4}");

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var sleepGoal = BuildSleepGoalState();
		var warmGoal = BuildStayWarmGoalState();
		var eatGoal = BuildEatFoodGoalState();
		
		BenchmarkSleepScenarios(sleepGoal);
		BenchmarkParallelGoals(sleepGoal, warmGoal, eatGoal);

		// Additional deep-dive benchmark: large batch planning to surface small per-plan changes.
		await BenchmarkPlanDeep(initialState, goalState);
	}

	/// <summary>
	/// Stress tests GOAP planning in a single tight loop to amplify per-plan costs.
	/// - Runs a large number of plans back-to-back without yielding.
	/// - Uses fixed initial/goal states to keep variance low.
	/// - Reports total time and ms/plan so small regressions are visible.
	/// </summary>
	private async Task BenchmarkPlanDeep(State initialState, State goalState)
	{
		const int deepPlanCount = 10_000;

		ulong startUsec = Time.GetTicksUsec();
		for (int i = 0; i < deepPlanCount; i++)
		{
			AdvancedGoalPlanner.ForwardPlan(initialState, goalState);
		}
		double totalMs = (Time.GetTicksUsec() - startUsec) / 1000.0;
		double msPerPlan = totalMs / deepPlanCount;

		GD.Print($"[PlanDeep] {deepPlanCount} plans -> {totalMs:F2} ms total ({msPerPlan:F4} ms/plan)");
		_results.Add($"GOAP_PlanDeep,{deepPlanCount},{totalMs:F2},{msPerPlan:F4}");

		// Yield once to keep the engine responsive after the long loop.
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private void SpawnBenchmarkEntities(int count)
	{
		if (count <= 0)
			return;

		for (int i = 0; i < count; i++)
		{
			var entity = EntityFactory.Create(Nature.SimpleTree);
			entity.Transform.Position = Game.Utils.Random.NextVector2(-5_000, 5_000);
			EntityManager.Instance.RegisterEntity(entity);
			_benchmarkEntities.Add(entity);
		}
	}

	private void ExportResults()
	{
		string projectPath = ProjectSettings.GlobalizePath("res://");
		string timestampDir = DateTime.Now.ToString("MMMM-d-h-mmtt");
		string dirPath = System.IO.Path.Combine(projectPath, "Benchmarks", timestampDir);
		System.IO.Directory.CreateDirectory(dirPath);
		string filePath = System.IO.Path.Combine(dirPath, $"performance_benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

		try
		{
			System.IO.File.WriteAllLines(filePath, _results);
			GD.Print($"[PerformanceBenchmark] Results written to {filePath}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[PerformanceBenchmark] Failed to export CSV: {ex.Message}");
		}
	}

	private void BenchmarkSleepScenarios(State goal)
	{
		_results.Add("Section,Scenario,PlanSteps,PlanMs");

		foreach (var scenario in SleepScenarios)
		{
			var initial = CreateBaseSleepState();
			scenario.Configure(initial);
			var plan = MeasurePlan(initial, goal, out double elapsedMs);
			int steps = plan?.Steps.Count ?? 0;
			GD.Print($"Scenario {scenario.Name}: {steps} steps in {elapsedMs:F3} ms");
			_results.Add($"GOAP_Sleep,{scenario.Name},{steps},{elapsedMs:F3}");
		}
	}

	private void BenchmarkParallelGoals(State sleepGoal, State warmGoal, State eatGoal)
	{
		_results.Add("Section,Agents,TotalMs,MsPerPlan,AvgSteps,FailedPlans");

		foreach (var agentBatch in ParallelAgentBatches)
		{
			var random = new StdRandom(1_337 + agentBatch);
			var scenarios = new (State Initial, State Goal)[agentBatch];
			
			for (int i = 0; i < agentBatch; i++)
			{
				double r = random.NextDouble();
				if (r < 0.33)
				{
					scenarios[i] = (CreateRandomizedSleepState(random, i), sleepGoal);
				}
				else if (r < 0.66)
				{
					scenarios[i] = (CreateRandomizedStayWarmState(random, i), warmGoal);
				}
				else
				{
					scenarios[i] = (CreateRandomizedEatFoodState(random, i), eatGoal);
				}
			}

			long totalSteps = 0;
			long completedPlans = 0;
			long failedPlans = 0;
			ulong startUsec = Time.GetTicksUsec();

			Parallel.For(0, agentBatch, idx =>
			{
				var (initial, goal) = scenarios[idx];
				var plan = AdvancedGoalPlanner.ForwardPlan(initial, goal);
				if (plan != null)
				{
					Interlocked.Add(ref totalSteps, plan.Steps.Count);
					Interlocked.Increment(ref completedPlans);
				}
				else
				{
					Interlocked.Increment(ref failedPlans);
				}
			});

			double totalMs = (Time.GetTicksUsec() - startUsec) / 1000.0;
			double msPerPlan = completedPlans > 0 ? totalMs / completedPlans : 0;
			double avgSteps = completedPlans > 0 ? (double)totalSteps / completedPlans : 0;

			GD.Print($"Parallel {agentBatch} agents -> {totalMs:F2} ms total ({msPerPlan:F4} ms/plan, {avgSteps:F2} avg steps, {failedPlans} failed plans)");
			_results.Add($"GOAP_ParallelAgents,{agentBatch},{totalMs:F2},{msPerPlan:F4},{avgSteps:F2},{failedPlans}");
		}
	}

	private static State BuildSleepGoalState()
	{
		var goal = new State();
		goal.Set("IsSleepy", false);
		return goal;
	}

	private static State BuildStayWarmGoalState()
	{
		var goal = new State();
		goal.Set(FactKeys.NearTarget(Tags.Campfire), true);
		return goal;
	}

	private static State BuildEatFoodGoalState()
	{
		var goal = new State();
		goal.Set("IsHungry", false);
		return goal;
	}

	private static State CreateBaseSleepState()
	{
		var state = new State();
		state.Set("IsSleepy", true);
		state.Set(FactKeys.NearTarget(Tags.Bed), false);
		state.Set(FactKeys.WorldHas(Tags.Bed), false);
		state.Set(FactKeys.WorldCount(Tags.Bed), 0);
		state.Set(FactKeys.AgentHas(Tags.Stick), false);
		state.Set(FactKeys.AgentCount(Tags.Stick), 0);
		state.Set(FactKeys.WorldHas(Tags.Stick), false);
		state.Set(FactKeys.WorldCount(Tags.Stick), 0);
		state.Set(FactKeys.WorldHas(Tags.Tree), false);
		state.Set(FactKeys.WorldCount(Tags.Tree), 0);
		state.Set(FactKeys.NearTarget(Tags.Tree), false);
		state.Set(FactKeys.NearTarget(Tags.Stick), false);
		return state;
	}

	private static State CreateBaseStayWarmState()
	{
		var state = new State();
		state.Set(FactKeys.NearTarget(Tags.Campfire), false);
		state.Set(FactKeys.WorldHas(Tags.Campfire), false);
		state.Set(FactKeys.WorldCount(Tags.Campfire), 0);
		
		// Campfire needs 2 sticks.
		state.Set(FactKeys.AgentHas(Tags.Stick), false);
		state.Set(FactKeys.AgentCount(Tags.Stick), 0);
		state.Set(FactKeys.WorldHas(Tags.Stick), false);
		state.Set(FactKeys.WorldCount(Tags.Stick), 0);
		state.Set(FactKeys.WorldHas(Tags.Tree), false);
		state.Set(FactKeys.WorldCount(Tags.Tree), 0);
		state.Set(FactKeys.NearTarget(Tags.Tree), false);
		state.Set(FactKeys.NearTarget(Tags.Stick), false);
		return state;
	}

	private static State CreateBaseEatFoodState()
	{
		var state = new State();
		state.Set("IsHungry", true);
		state.Set(FactKeys.NearTarget(Tags.Food), false);
		state.Set(FactKeys.WorldHas(Tags.Food), false);
		state.Set(FactKeys.WorldCount(Tags.Food), 0);
		return state;
	}

	private static State CreateRandomizedSleepState(StdRandom random, int agentId)
	{
		var state = CreateBaseSleepState();

		// Randomize agent inventory and proximity.
		bool agentHasStick = random.NextDouble() < 0.45;
		int agentStickCount = agentHasStick ? random.Next(1, 6) : 0;
		state.Set(FactKeys.AgentHas(Tags.Stick), agentHasStick);
		state.Set(FactKeys.AgentCount(Tags.Stick), agentStickCount);
		state.Set(FactKeys.NearTarget(Tags.Stick), random.NextDouble() < 0.35);
		state.Set(FactKeys.NearTarget(Tags.Tree), random.NextDouble() < 0.35);

		// Randomize world availability for primary resources.
		// Ensure sticks are more available (beds need 4 sticks vs campfire's 2)
		bool worldHasSticks = random.NextDouble() < 0.85;
		state.Set(FactKeys.WorldHas(Tags.Stick), worldHasSticks);
		state.Set(FactKeys.WorldCount(Tags.Stick), worldHasSticks ? random.Next(4, 20) : 0);

		// Trees should be almost always available as the fallback resource
		bool worldHasTrees = random.NextDouble() < 0.99;
		state.Set(FactKeys.WorldHas(Tags.Tree), worldHasTrees);
		state.Set(FactKeys.WorldCount(Tags.Tree), worldHasTrees ? random.Next(20, 100) : 0);

		bool worldHasBed = random.NextDouble() < 0.3;
		state.Set(FactKeys.WorldHas(Tags.Bed), worldHasBed);
		state.Set(FactKeys.WorldCount(Tags.Bed), worldHasBed ? random.Next(1, 3) : 0);
		state.Set(FactKeys.NearTarget(Tags.Bed), worldHasBed && random.NextDouble() < 0.2);

		// Inject per-agent identity to avoid clone states.
		state.Set(FactKeys.AgentId, agentId);
		state.Set(FactKeys.Position, random.Next(-10_000, 10_001));

		return state;
	}

	private static State CreateRandomizedStayWarmState(StdRandom random, int agentId)
	{
		var state = CreateBaseStayWarmState();

		// Similar stick distribution as sleep, but campfire needs fewer (2).
		bool agentHasStick = random.NextDouble() < 0.45;
		int agentStickCount = agentHasStick ? random.Next(1, 4) : 0;
		state.Set(FactKeys.AgentHas(Tags.Stick), agentHasStick);
		state.Set(FactKeys.AgentCount(Tags.Stick), agentStickCount);
		state.Set(FactKeys.NearTarget(Tags.Stick), random.NextDouble() < 0.35);
		state.Set(FactKeys.NearTarget(Tags.Tree), random.NextDouble() < 0.35);

		bool worldHasSticks = random.NextDouble() < 0.85;
		state.Set(FactKeys.WorldHas(Tags.Stick), worldHasSticks);
		state.Set(FactKeys.WorldCount(Tags.Stick), worldHasSticks ? random.Next(2, 15) : 0);

		bool worldHasTrees = random.NextDouble() < 0.99;
		state.Set(FactKeys.WorldHas(Tags.Tree), worldHasTrees);
		state.Set(FactKeys.WorldCount(Tags.Tree), worldHasTrees ? random.Next(10, 50) : 0);

		bool worldHasCampfire = random.NextDouble() < 0.3;
		state.Set(FactKeys.WorldHas(Tags.Campfire), worldHasCampfire);
		state.Set(FactKeys.WorldCount(Tags.Campfire), worldHasCampfire ? random.Next(1, 3) : 0);
		state.Set(FactKeys.NearTarget(Tags.Campfire), worldHasCampfire && random.NextDouble() < 0.2);

		state.Set(FactKeys.AgentId, agentId);
		state.Set(FactKeys.Position, random.Next(-10_000, 10_001));

		return state;
	}

	private static State CreateRandomizedEatFoodState(StdRandom random, int agentId)
	{
		var state = CreateBaseEatFoodState();

		// Food availability
		bool worldHasFood = random.NextDouble() < 0.90; // Food is fairly common for this test
		state.Set(FactKeys.WorldHas(Tags.Food), worldHasFood);
		state.Set(FactKeys.WorldCount(Tags.Food), worldHasFood ? random.Next(1, 10) : 0);
		state.Set(FactKeys.NearTarget(Tags.Food), worldHasFood && random.NextDouble() < 0.4);

		state.Set(FactKeys.AgentId, agentId);
		state.Set(FactKeys.Position, random.Next(-10_000, 10_001));

		return state;
	}

	private static Plan MeasurePlan(State initialState, State goalState, out double elapsedMs)
	{
		ulong startUsec = Time.GetTicksUsec();
		var plan = AdvancedGoalPlanner.ForwardPlan(initialState, goalState);
		elapsedMs = (Time.GetTicksUsec() - startUsec) / 1000.0;
		return plan;
	}
}

