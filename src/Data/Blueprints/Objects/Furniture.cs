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

    // Removed YSorted variant; VisualComponent now renders via CustomEntityRenderEngine
}