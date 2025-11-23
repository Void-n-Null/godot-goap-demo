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

namespace Game.Universe;

/// <summary>
/// Purpose-built benchmarking harness for producing headline graphs (ECS throughput,
/// QuadTree allocation profile, GOAP planning latency). Press 'B' in-game to run.
/// </summary>
public partial class TorvieBenchmark : Node
{
	private readonly struct StayWarmScenario
	{
		public string Name { get; }
		public Action<State> Configure { get; }

		public StayWarmScenario(string name, Action<State> configure)
		{
			Name = name;
			Configure = configure;
		}
	}

	private static readonly int[] EcsEntityTargets =
	{
		1_000, 5_000, 10_000, 20_000, 40_000, 60_000, 80_000, 100_000, 125_000, 150_000
	};

	private static readonly int[] QuadTreeQueryBatches = { 500, 1_000, 2_000, 4_000 };

private static readonly int[] ParallelAgentBatches = { 50, 100, 200, 500, 1_000 };

	private static readonly StayWarmScenario[] StayWarmScenarios =
	{
		new("CampfireNearby", state =>
		{
			state.Set(FactKeys.WorldHas(TargetType.Campfire), true);
			state.Set(FactKeys.WorldCount(TargetType.Campfire), 1);
			// Need to walk to an existing fire.
		}),
		new("InventoryHasSticks", state =>
		{
			state.Set(FactKeys.AgentHas(TargetType.Stick), true);
			state.Set(FactKeys.AgentCount(TargetType.Stick), 2);
		}),
		new("GatherDroppedSticks", state =>
		{
			state.Set(FactKeys.WorldHas(TargetType.Stick), true);
			state.Set(FactKeys.WorldCount(TargetType.Stick), 6);
		}),
		new("FullPipelineFromTrees", state =>
		{
			state.Set(FactKeys.WorldHas(TargetType.Tree), true);
			state.Set(FactKeys.WorldCount(TargetType.Tree), 40);
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
		GD.Print("[TorvieBenchmark] Ready. Press 'B' to run architectural benchmarks.");
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
			GD.Print("[TorvieBenchmark] Benchmark already running or completed.");
			return;
		}

		if (!EntityManager.HasInstance)
		{
			GD.PrintErr("[TorvieBenchmark] EntityManager is not ready. Cannot run benchmarks.");
			return;
		}

		_state = BenchmarkState.Running;
		_results.Clear();
		_benchmarkEntities.Clear();

		GD.Print("\n[ Torvie ] === STARTING ARCHITECTURAL BENCHMARK SUITE ===");

		await CleanupWorld();
		await RunEcsThroughputTest();
		await RunQuadTreeQueryTest();
		await RunGoapPlannerTest();
		ExportResults();

		GD.Print("[ Torvie ] === BENCHMARK COMPLETE ===");
		_state = BenchmarkState.Complete;
	}

	private async Task CleanupWorld()
	{
		var manager = EntityManager.Instance;

		var existing = new List<IUpdatableEntity>(manager.GetEntities());
		GD.Print($"[TorvieBenchmark] Clearing {existing.Count} existing entities.");

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
		_results.Add("Section,ECS_Count,ECS_UpdateMs,ECS_FrameMs");

		foreach (var targetCount in EcsEntityTargets)
		{
			int needed = targetCount - _benchmarkEntities.Count;
			if (needed > 0)
			{
				SpawnBenchmarkEntities(needed);
			}

			// let components settle
			for (int i = 0; i < 5; i++)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			const int samples = 60;
			double totalUpdateMs = 0;
			double totalFrameBudget = 0;

			for (int i = 0; i < samples; i++)
			{
				ulong startUsec = Time.GetTicksUsec();
				for (int j = 0; j < _benchmarkEntities.Count; j++)
				{
					_benchmarkEntities[j].Update(1.0 / 60.0);
				}

				ulong endUsec = Time.GetTicksUsec();
				totalUpdateMs += (endUsec - startUsec) / 1000.0;
				totalFrameBudget += 1000.0 / Math.Max(1.0, Engine.GetFramesPerSecond());

				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			double avgUpdate = totalUpdateMs / samples;
			double avgFrame = totalFrameBudget / samples;

			GD.Print($"Count {targetCount:N0} | Update {avgUpdate:F2} ms | Frame {avgFrame:F2} ms");
			_results.Add($"ECS,{targetCount},{avgUpdate:F2},{avgFrame:F2}");
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

		// Warm planner caches
		AdvancedGoalPlanner.ForwardPlan(initialState, goalState);

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

		var stayWarmGoal = BuildStayWarmGoalState();
		BenchmarkStayWarmScenarios(stayWarmGoal);
		BenchmarkParallelStayWarm(stayWarmGoal);
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
		string filePath = System.IO.Path.Combine(projectPath, $"torvie_benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

		try
		{
			System.IO.File.WriteAllLines(filePath, _results);
			GD.Print($"[TorvieBenchmark] Results written to {filePath}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[TorvieBenchmark] Failed to export CSV: {ex.Message}");
		}
	}

	private void BenchmarkStayWarmScenarios(State goal)
	{
		_results.Add("Section,Scenario,PlanSteps,PlanMs");

		foreach (var scenario in StayWarmScenarios)
		{
			var initial = CreateBaseStayWarmState();
			scenario.Configure(initial);
			var plan = MeasurePlan(initial, goal, out double elapsedMs);
			int steps = plan?.Steps.Count ?? 0;
			GD.Print($"Scenario {scenario.Name}: {steps} steps in {elapsedMs:F3} ms");
			_results.Add($"GOAP_StayWarm,{scenario.Name},{steps},{elapsedMs:F3}");
		}
	}

	private void BenchmarkParallelStayWarm(State goalState)
	{
		_results.Add("Section,Agents,TotalMs,MsPerPlan,AvgSteps,FailedPlans");

		foreach (var agentBatch in ParallelAgentBatches)
		{
			long totalSteps = 0;
			long completedPlans = 0;
			long failedPlans = 0;
			ulong startUsec = Time.GetTicksUsec();

			Parallel.For(0, agentBatch, _ =>
			{
				var initial = CreateFullPipelineStayWarmState();
				var plan = AdvancedGoalPlanner.ForwardPlan(initial, goalState);
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

	private static State BuildStayWarmGoalState()
	{
		var goal = new State();
		goal.Set(FactKeys.NearTarget(TargetType.Campfire), true);
		return goal;
	}

	private static State CreateBaseStayWarmState()
	{
		var state = new State();
		state.Set(FactKeys.NearTarget(TargetType.Campfire), false);
		state.Set(FactKeys.WorldHas(TargetType.Campfire), false);
		state.Set(FactKeys.WorldCount(TargetType.Campfire), 0);
		state.Set(FactKeys.AgentHas(TargetType.Stick), false);
		state.Set(FactKeys.AgentCount(TargetType.Stick), 0);
		state.Set(FactKeys.WorldHas(TargetType.Stick), false);
		state.Set(FactKeys.WorldCount(TargetType.Stick), 0);
		state.Set(FactKeys.WorldHas(TargetType.Tree), false);
		state.Set(FactKeys.WorldCount(TargetType.Tree), 0);
		state.Set(FactKeys.NearTarget(TargetType.Tree), false);
		state.Set(FactKeys.NearTarget(TargetType.Stick), false);
		return state;
	}

	private static State CreateFullPipelineStayWarmState()
	{
		var state = CreateBaseStayWarmState();
		state.Set(FactKeys.WorldHas(TargetType.Tree), true);
		state.Set(FactKeys.WorldCount(TargetType.Tree), 50);
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
