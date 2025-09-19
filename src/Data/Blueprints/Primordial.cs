using System;
using Godot;
using Game.Data.Components;
using Game.Data;
using System.Collections.Generic;

namespace Game.Data.Blueprints;

/// <summary>
/// 
/// This class contains the base entities and components that are used to create the game world.
/// Every entity should derive from one of these base entities. (Which means technically every entity should derive from BaseEntity)
/// </summary>
public static class Primordial
{
    public static readonly EntityBlueprint BaseEntity = new( name: "BaseEntity")
    {
        DuplicatePolicies = new Dictionary<Type, DuplicatePolicy>
        {
            { typeof(TransformComponent2D), DuplicatePolicy.Prohibit },
            { typeof(VisualComponent), DuplicatePolicy.Prohibit },
            { typeof(BatchedVisualComponent), DuplicatePolicy.Prohibit },
            { typeof(MovementComponent), DuplicatePolicy.Prohibit },
            { typeof(HealthComponent), DuplicatePolicy.Prohibit },
        }
    };


    // 2D: transform only
    public static readonly EntityBlueprint Entity2D = BaseEntity.Derive(name: "Entity2D",
        addComponents: () => [ new TransformComponent2D() ]
    );

        // Visual layer (no components by itself; specialized types add visuals)
    public static readonly EntityBlueprint EmbodiedEntity = Entity2D.Derive(
        name: "VisualEntity2D",
        addComponents: () => [
            new VisualComponent(null) { ScaleMultiplier = new Vector2(0.2f, 0.2f) }
        ]
    );

}