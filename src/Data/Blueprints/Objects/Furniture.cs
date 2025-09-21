using Godot;
using Game.Data.Blueprints;
using Game.Data.Components;
using Game.Utils;

namespace Game.Data.Blueprints.Objects;

public static class Furniture
{
    public static readonly EntityBlueprint BaseFurniture = Primordial.EmbodiedEntity.Derive(
        name: "BaseFurniture",
        addTags: [ Tags.Furniture, Tags.Flammable ]
    );

    public static readonly EntityBlueprint Bed = BaseFurniture.Derive(
        name: "Bed",
        addTags: [ Tags.CanBeSleptIn ],
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = "res://textures/Bed.png")
        ]
    );

    // Immediate-mode variant: no nodes per entity, Y-sorted rendering
    public static readonly EntityBlueprint BedYSorted = Primordial.Entity2D.Derive(
        name: "BedYSorted",
        addTags: [ Tags.Furniture, Tags.Flammable, Tags.CanBeSleptIn ],
        addComponents: () => [ new YSortedVisualComponent(Resources.GetTexture("res://textures/Bed.png"), new Vector2(0.2f, 0.2f)) ]
    );
}