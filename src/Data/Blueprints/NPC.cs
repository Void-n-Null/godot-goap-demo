using Game.Data.Components;
using Godot;
using Game.Utils;
namespace Game.Data.Blueprints;


public static class NPC
{
    // Base: no components

    // Girl: visual-only entity with sprite (node-based rendering)
    public static readonly EntityBlueprint Girl = Primordial.EmbodiedEntity.Derive(
        name: "Girl",
        addComponents: () => [
            new MovementComponent(MaxSpeed: 300f * Utils.Random.NextFloat(0.5f, 1.5f), Friction: 1f)
        ],
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = Utils.Random.NextItem([
                "res://textures/Boy.png",
                "res://textures/Female.png",
                "res://textures/Girl.png",
                "res://textures/Male.png"
            ]))
        ]
    );

    // Removed YSorted variant; VisualComponent now renders via CustomEntityRenderEngine
}


