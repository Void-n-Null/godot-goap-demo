using System;
using System.Collections.Generic;
using Game.Data.Components;
using Game.Utils;
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
            { typeof(BatchedVisualComponent), DuplicatePolicy.Prohibit },
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

    // Batched visual layer: entities will use BatchedVisualComponent
    public static readonly EntityBlueprint BatchedVisualEntity2D = Entity2D.Derive(
        name: "BatchedVisualEntity2D"
    );

    // Girl: visual-only entity with sprite
    public static readonly EntityBlueprint Girl = BatchedVisualEntity2D.Derive(
        name: "Girl",
        addComponents: () => [
            new BatchedVisualComponent(Resources.GetTexture("res://textures/Girl.png")) { ScaleMultiplier = new Vector2(0.2f, 0.2f) },
            new MovementComponent(MaxSpeed: 300f * Utils.Random.NextFloat(0.5f, 1.5f), Friction: 1f)
        ]
    );
}


