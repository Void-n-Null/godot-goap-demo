using Game.Data;
using Game.Data.Blueprints;
using Game.Data.Blueprints.Objects;
using Game.Data.Components;
using Game.Universe;
using Game.Utils;
using Godot;

namespace Game.Data.Blueprints;

public static class Nature
{
    private static readonly Vector2 TreeScale = Vector2.One * 0.45f;
    private static readonly Vector2 TreeVisualOffset = new(0, -100f);
    private static readonly float TreeZBias = 100f;

    public static readonly EntityBlueprint TreeStump = Primordial.EmbodiedEntity.Derive(
        name: "TreeStump",
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) =>
            {
                c.PendingSpritePath = "res://textures/Stump.png";
                c.ScaleMultiplier = TreeScale;
                c.VisualOriginOffset = TreeVisualOffset;
                c.ZBiasOverride = TreeZBias;
            })
        ]
    );

    public static readonly EntityBlueprint BaseTree = Primordial.EmbodiedEntity.Derive(
        name: "BaseTree",
        addTags: [ Tags.Tree, Tags.Wooden ],
        addComponents: () =>
        {
            var health = new HealthComponent(maxHealth: 200); // More durable
            health.OnDeathAction += () => SpawnTreeStump(health.Entity);

            return new IComponent[]
            {
                health,
                new TargetComponent(TargetType.Tree),
                new LootDropComponent 
                { 
                    Drops = 
                    [
                        new LootDrop(Items.Stick, quantity: 4) // Drops 4 sticks
                    ],
                    ScatterRadius = 50f // Tight scatter so agent can pick up all from one position
                }
            };
        }
    );

    public static readonly EntityBlueprint SimpleTree = BaseTree.Derive(
        name: "SimpleTree",
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = "res://textures/Tree.png"), // Replace with actual tree texture path
            EntityBlueprint.Mutate<VisualComponent>((c) => c.ScaleMultiplier = TreeScale),
            EntityBlueprint.Mutate<VisualComponent>((c) => c.VisualOriginOffset = TreeVisualOffset),
            EntityBlueprint.Mutate<VisualComponent>((c) => c.ZBiasOverride = TreeZBias)
        ]
    );

    private static void SpawnTreeStump(Entity treeEntity)
    {
        var transform = treeEntity?.Transform;
        if (transform == null) return;

        SpawnEntity.Now(TreeStump, transform.Position);
    }

    // Add more variants as needed, e.g., OakTree, PineTree with different visuals/health
}
