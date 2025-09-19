using System;
using System.Collections.Generic;
using Game.Data.Components;
using Godot;

namespace Game.Data;

public static class Blueprints
{
    // Base: no components
    public static readonly EntityBlueprint BaseEntity = new(
        name: "BaseEntity"
    )
    {
        DuplicatePolicies = new Dictionary<Type, DuplicatePolicy>
        {
            // One transform per entity — prohibit duplicates
            { typeof(TransformComponent2D), DuplicatePolicy.Prohibit },
            // One visual per entity given current storage — prefer customization via mutators
            { typeof(VisualComponent), DuplicatePolicy.Prohibit },
            // One movement logic per entity — prohibit duplicates
            { typeof(MovementComponent), DuplicatePolicy.Prohibit },
            // One health model per entity — prohibit duplicates
            { typeof(HealthComponent), DuplicatePolicy.Prohibit },
        }
    };

    // 2D: transform only
    public static readonly EntityBlueprint Entity2D = BaseEntity.Derive(
        name: "Entity2D",
        addComponents: () => [ new TransformComponent2D() ]
    );

    // Visual layer (no components by itself; specialized types add visuals)
    public static readonly EntityBlueprint VisualEntity2D = Entity2D.Derive(
        name: "VisualEntity2D",
        addComponents: () => [
            new VisualComponent(null) { ScaleMultiplier = new Vector2(0.2f, 0.2f) }
        ]
    );

    // Girl: visual-only entity with sprite
    public static readonly EntityBlueprint Girl = VisualEntity2D.Derive(
        name: "Girl",
        addComponents: () => [
            new MovementComponent(MaxSpeed: 300f, Friction: 1f)
        ],
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = "res://textures/Girl.png")
        ]
    );
}


