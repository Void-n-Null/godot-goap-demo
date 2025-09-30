using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Data.Components;
using Game.Utils;

namespace Game.Data.Blueprints;

public static class Nature
{
    public static readonly EntityBlueprint BaseTree = Primordial.EmbodiedEntity.Derive(
        name: "BaseTree",
        addTags: [ Tags.Tree, Tags.Wooden ],
        addComponents: () => [
            new HealthComponent(maxHealth: 200), // More durable
            new TargetComponent(TargetType.Tree),
            new LootDropComponent 
            { 
                Drops = 
                [
                    new LootDrop(Items.Stick, quantity: 4) // Drops 4 sticks
                ],
                ScatterRadius = 50f // Tight scatter so agent can pick up all from one position
            }
        ]
    );

    public static readonly EntityBlueprint SimpleTree = BaseTree.Derive(
        name: "SimpleTree",
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = "res://textures/Tree.png") // Replace with actual tree texture path
        ]
    );

    // Add more variants as needed, e.g., OakTree, PineTree with different visuals/health
}
