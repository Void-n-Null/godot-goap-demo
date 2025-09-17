using Godot;
using Game.Data;
using Game.Data.Components;

namespace Game.Data.Components;

/// <summary>
/// Visual representation component.
/// </summary>
public class VisualComponent(string ScenePath = null) : IComponent
{
    public Node2D ViewNode { get; private set; }
    public string ScenePath { get; set; } = ScenePath;

    // Cached reference to position component (set in PostAttached)
    private TransformComponent2D _transform2D;
    
    public Entity Entity { get; set; }



    public void Update(double delta)
    {
        if (ViewNode != null && _transform2D != null)
        {
            // Use cached position component reference (no type checking!)
            ViewNode.GlobalPosition = _transform2D.Position;
            ViewNode.GlobalRotation = _transform2D.Rotation;
            ViewNode.Scale = _transform2D.Scale;
        }
    }

    public void OnPreAttached()
    {
        // Phase 1: Create the visual node
        if (!string.IsNullOrEmpty(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            ViewNode = scene.Instantiate<Node2D>();
        }
        else
        {
            ViewNode = new Node2D();
        }

        // Add to scene (but don't sync position yet)
        // Note: This assumes EntityManager is available - might need adjustment
        // depending on how this is used
    }

    public void OnPostAttached()
    {
        // Phase 2: All components attached, safe to get position component
        _transform2D = Entity.GetComponent<TransformComponent2D>();

        if (_transform2D == null)
        {
            GD.PushWarning($"VisualComponent: No Transform2D found on entity {Entity.Id}. Visual sync disabled.");
        }
        else
        {
            // Initial position sync
            if (ViewNode != null)
            {
                ViewNode.GlobalPosition = _transform2D.Position;
                ViewNode.GlobalRotation = _transform2D.Rotation;
                ViewNode.Scale = _transform2D.Scale;
            }
        }
    }

    public void OnDetached()
    {
        if (ViewNode != null)
        {
            // Note: Scene management might need to be handled by the system using this component
            ViewNode.QueueFree();
            ViewNode = null;
        }
        _transform2D = null;
    }
}