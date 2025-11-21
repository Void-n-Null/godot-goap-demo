using Game.Data.Blueprints;
using Game.Data.Components;
using Godot;

namespace Game.Data.Blueprints.Objects;

public static class Campfire
{
    public static readonly EntityBlueprint BaseCampfire = Primordial.EmbodiedEntity.Derive(
        name: "BaseCampfire",
        addTags: [ Tags.Campfire, Tags.HeatSource, Tags.Flammable ],
        addComponents: () => [
            new TargetComponent(TargetType.Campfire),
            new HealthComponent(maxHealth: 100), // Can be extinguished/destroyed
            new FireVisualComponent(
                flameTexturePath: "res://textures/Flame.png",
                intensity: 1.2f,
                scaleMultiplier: new Vector2(0.3f, 0.2f),
                visualOffset: new Vector2(-5f, -20f),
                startActive: true
            )
        ]
    );

    public static readonly EntityBlueprint SimpleCampfire = BaseCampfire.Derive(
        name: "Campfire",
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = "res://textures/Campfire.png"),
            EntityBlueprint.Mutate<VisualComponent>((c) => c.VisualOriginOffset = new Vector2(0, -20f))
        ]
    );
}
