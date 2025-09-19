using Godot;
using Game.Data;
using Game.Data.Components;
using Game.Utils;

namespace Game.Data.Components;

/// <summary>
/// Visual representation component.
/// </summary>
public class VisualComponent(string ScenePath = null) : IActiveComponent
{
	public Node2D ViewNode { get; private set; }
	public string ScenePath { get; set; } = ScenePath;
	public Node ParentNode { get; set; } // optional injection by caller

	public Vector2? ScaleMultiplier { get; set; }

	public Sprite2D Sprite { get; private set; }
	public string PendingSpritePath { get; set; }

	// Cached reference to position component (set in PostAttached)
	private TransformComponent2D _transform2D;
	
	public Entity Entity { get; set; }

	public void SetSprite(string path)
	{
		SetSprite(Resources.GetTexture(path));
	}

	public void SetSprite(Texture2D texture)
	{
		if (texture == null) return;
		EnsureSpriteNode();
		if (Sprite != null)
		{
			Sprite.Texture = texture;
		}
	}

	public void Update(double delta)
	{
		if (ViewNode != null && _transform2D != null)
		{
			// Use cached position component reference (local space for consistency)
			ViewNode.Position = _transform2D.Position;
			ViewNode.Rotation = _transform2D.Rotation;
			ViewNode.Scale = _transform2D.Scale * (ScaleMultiplier ?? Vector2.One);
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
		else if (ViewContext.DefaultViewScene != null)
		{
			ViewNode = ViewContext.DefaultViewScene.Instantiate<Node2D>();
		}
		else
		{
			// Fallback: create a simple Node2D with a Sprite2D child named "Sprite"
			var node = new Node2D();
			var sprite = new Sprite2D();
			sprite.Name = "Sprite";
			node.AddChild(sprite);
			ViewNode = node;
		}

		// Cache Sprite2D reference from root or child
		Sprite = (ViewNode as Sprite2D) ?? ViewNode.GetNodeOrNull<Sprite2D>("Sprite") ?? FindFirstSprite(ViewNode);

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
			// Ensure the view is in the scene tree
			if (ViewNode != null && ViewNode.GetParent() == null)
			{
				var parent = ParentNode ?? ViewContext.DefaultParent;
				if (parent != null)
				{
					parent.AddChild(ViewNode);
				}
			}

			// Initial transform sync (local space)
			if (ViewNode != null)
			{
				ViewNode.Position = _transform2D.Position;
				ViewNode.Rotation = _transform2D.Rotation;
				ViewNode.Scale = _transform2D.Scale;
			}

			// Apply pending sprite if provided
			if (!string.IsNullOrEmpty(PendingSpritePath))
			{
				SetSprite(PendingSpritePath);
				PendingSpritePath = null;
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

	private void EnsureSpriteNode()
	{
		if (Sprite != null) return;
		if (ViewNode is Sprite2D rootSprite)
		{
			Sprite = rootSprite;
			return;
		}
		// Try find any Sprite2D under the view
		Sprite = FindFirstSprite(ViewNode);
		if (Sprite != null) return;
		// Create one if still missing
		var sprite = new Sprite2D { Name = "Sprite" };
		ViewNode.AddChild(sprite);
		Sprite = sprite;
	}

	private Sprite2D FindFirstSprite(Node node)
	{
		if (node == null) return null;
		foreach (Node child in node.GetChildren())
		{
			if (child is Sprite2D s) return s;
		}
		return null;
	}
}
