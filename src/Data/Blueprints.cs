using Game.Data.Components;

namespace Game.Data;

public static class Blueprints
{
    // Base: no components
    public static readonly EntityBlueprint BaseEntity = new(
        name: "BaseEntity"
    );

    // 2D: transform only
    public static readonly EntityBlueprint Entity2D = BaseEntity.Derive(
        name: "Entity2D",
        addComponents: () => new IComponent[] { new TransformComponent2D() }
    );

    // Visual layer (no components by itself; specialized types add visuals)
    public static readonly EntityBlueprint VisualEntity2D = Entity2D.Derive(
        name: "VisualEntity2D"
    );

    // Girl: visual-only entity with sprite
    public static readonly EntityBlueprint Girl = VisualEntity2D.Derive(
        name: "Girl",
        addComponents: () => new IComponent[]
        {
            new VisualComponent(null) { PendingSpritePath = "res://textures/Girl.png" },
            new MovementComponent(MaxSpeed: 150f, Friction: 1f)
        }
    );
}


