using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Data;
using Game.Data.Components;
using Game.Utils;
using Game.Universe.Scenarios;

namespace Game.Universe;

/// <summary>
/// A singleton class that manages the game logic.
/// </summary>
public partial class GameManager : SingletonNode<GameManager>
{
    [Export] public bool DebugMode = false;
    [Export] public string StartingScenario = "ThreePeopleManyFood";
    /// <summary>
    /// Event that fires every physics tick. Subscribe to this to receive tick updates.
    /// </summary>
    /// <param name="callback">The callback to subscribe to</param>
    private event Action<double> Frame;
    [Export] int TickRatePerSecond = 60;
    [Export] int MaxTicksPerFrame = 4; // max ticks processed per frame when catching up
    [Export] double MaxAccumulatedTimeSeconds = 0.25; // cap backlog to ~15 ticks at 60 Hz
    [Export(PropertyHint.Range, "0.01,1,0.01")] float FrameTimeSmoothing = 0.1f;

    private event Action<double> Tick;
    private double _accumulatedTime = 0.0;
    private double TickInterval => 1.0 / TickRatePerSecond;
    private double _smoothedFrameTime = 1.0 / 60.0;
    public double SmoothedFps { get; private set; } = 60.0;
    public ulong CachedTimeMsec { get; private set; } = 0;

    // Scale Profiling System
    private ScaleProfiler _scaleProfiler;


    public void SubscribeToTick(Action<double> callback)
    {
        Tick += callback;
    }

    public void UnsubscribeFromTick(Action<double> callback)
    {
        Tick -= callback;
    }

    public void SubscribeToFrame(Action<double> callback)
    {
        Frame += callback;
    }

    public void UnsubscribeFromFrame(Action<double> callback)
    {
        Frame -= callback;
    }

    public override void _Ready()
    {
        Utils.Random.Initialize();
        LM.Info("GameManager: Ready");
        base._Ready();

        if (DebugMode)
        {
            LM.Info("GameManager: DebugMode enabled");
            AddChild(new StatsOverlay());
            AddChild(new EntityDebugTooltip());
        }

        // Ensure the TaskScheduler exists in the scene
        if (!TaskScheduler.HasInstance)
        {
            AddChild(new TaskScheduler());
        }

        // Initialize the ScaleProfiler
        _scaleProfiler = new ScaleProfiler();
        AddChild(_scaleProfiler);

        // Load and setup the selected scenario
        LoadScenario(StartingScenario);
    }

    /// <summary>
    /// Discovers all available scenario types using reflection
    /// </summary>
    public static Dictionary<string, Type> DiscoverScenarios()
    {
        var scenarioTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Scenario)))
            .ToDictionary(
                t => t.Name.Replace("Scenario", ""), // "DefaultScenario" -> "Default"
                t => t
            );

        return scenarioTypes;
    }

    private void LoadScenario(string scenarioName)
    {
        var availableScenarios = DiscoverScenarios();

        // Log available scenarios on first load
        LM.Debug($"Available scenarios: {string.Join(", ", availableScenarios.Keys)}");

        // Try to find and instantiate the requested scenario
        if (!availableScenarios.TryGetValue(scenarioName, out var scenarioType))
        {
            LM.Warning($"Scenario '{scenarioName}' not found. Falling back to 'Default'.");
            scenarioType = availableScenarios.GetValueOrDefault("Default");
        }

        if (scenarioType == null)
        {
            LM.Error("No scenarios found! Please create at least one scenario class.");
            return;
        }

        var scenario = (Scenario)Activator.CreateInstance(scenarioType);
        LM.Info($"Loading scenario: {scenario.Name}");
        LM.Info($"Description: {scenario.Description}");
        scenario.Setup();
    }

    /// <summary>
    /// Switches to a different scenario by wiping all entities and loading the new scenario
    /// </summary>
    public void SwitchScenario(string scenarioName)
    {
        LM.Info($"Switching to scenario: {scenarioName}");

        // Wipe all existing entities
        WipeAllEntities();

        // Load the new scenario
        LoadScenario(scenarioName);
    }

    /// <summary>
    /// Wipes all entities from the world
    /// </summary>
    private void WipeAllEntities()
    {
        if (EntityManager.Instance == null)
        {
            LM.Warning("EntityManager not found, cannot wipe entities");
            return;
        }

        var entities = EntityManager.Instance.GetEntities();
        var entityCount = entities.Count;

        LM.Info($"Wiping {entityCount} entities...");

        // Create a copy of the list to avoid modification during iteration
        var entitiesToRemove = new List<IUpdatableEntity>(entities);

        foreach (var entity in entitiesToRemove)
        {
            // If it's a concrete Entity, properly clean up components
            if (entity is Entity concreteEntity)
            {
                // Call OnDetached for all components to ensure proper cleanup
                // This will trigger RemoveSprite() for VisualComponent and FireVisualComponent
                var components = concreteEntity.GetAllComponents();
                foreach (var component in components)
                {
                    component.OnDetached();
                }

                // Remove visual component's ViewNode if it exists
                if (concreteEntity.TryGetComponent<VisualComponent>(out var visual))
                {
                    visual.ViewNode?.QueueFree();
                }
            }

            // Unregister from EntityManager
            EntityManager.Instance.UnregisterEntity(entity);
        }

        LM.Info($"Wiped {entityCount} entities");
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        CachedTimeMsec = Time.GetTicksMsec();

        FrameTime.Advance((float)delta);

        UpdateSmoothedFrameTime(delta);

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
            int ticksProcessed = 0;
            while (_accumulatedTime >= TickInterval && ticksProcessed < MaxTicksPerFrame)
            {
                _accumulatedTime -= TickInterval;
                Tick?.Invoke(TickInterval);
                ticksProcessed++;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        base._UnhandledInput(@event);

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.G)
            {
                _scaleProfiler?.StartProfiling();
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
}