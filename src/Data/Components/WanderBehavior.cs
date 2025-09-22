using Godot;
using Game.Data.Components;
using Game.Data;
using System;
using Game.Utils;   
using Game.Universe;
using Random = Game.Utils.Random;
using System.Collections.Generic;

namespace Game.Data.Components;


//The entity shall blindly follow the mouse via its NPCMotorComponent
public class WanderBehavior(float radius = 100f) : IActiveComponent
{
    public Entity Entity { get; set; }
    public float Radius { get; set; } = radius;
    private NPCMotorComponent _motor;
    private TransformComponent2D _transform;
    public bool isRelaxed = false;
    private Vector2? _currentTarget;

    public void OnPostAttached()
    {
        _motor = this.GetComponent<NPCMotorComponent>();
        _transform = this.GetComponent<TransformComponent2D>();
        _motor.OnTargetReached += OnTargetReached;
    }

    public void OnStart()
    {
        GD.Print("WanderBehavior: OnStart");
        StartNewWander();
    }

    public void StartNewWander()
    {
        _currentTarget = Random.InsideCircle(_transform.Position, Radius);
        _motor.Target = _currentTarget;
    }

    public IEnumerable<ComponentDependency> GetRequiredComponents()
    {
        yield return new ComponentDependency(typeof(NPCMotorComponent));
        yield return new ComponentDependency(typeof(TransformComponent2D));
    }

    public void OnTargetReached(){
        _motor.Target = null;
        var seconds = Random.NextFloat(1.0f, 3.0f);
        TaskScheduler.Instance.ScheduleSeconds(StartNewWander, seconds);
    }

    public void OnPostAttachedDraw()
    {
        // no-op; kept for parity
    }

    public void UpdateDebugDraw()
    {
        if (_motor == null || _transform == null) return;
        if (!(_motor.DebugDrawEnabled)) return;
        if (Utils.CustomEntityRenderEngineLocator.Renderer == null) return;


        // Visualize target circle and line to center when target exists
        var target = _motor.Target ?? _currentTarget;
        if (target != null)
        {
            var center = target.Value;
            var radius = _motor.TargetReachedRadius;
            Utils.CustomEntityRenderEngineLocator.Renderer.QueueDebugCircle(center, radius, Colors.LimeGreen, 1.5f, 36);
            Utils.CustomEntityRenderEngineLocator.Renderer.QueueDebugLine(_transform.Position, center, Colors.Cyan, 1.5f);
        }
    }

    public void Update(double delta)
    {
        UpdateDebugDraw();
    }
}