using Game.Data.Blueprints;
using Game.Data.Components;
using TG = Game.Data.Tags;

namespace Game.Data.Blueprints.Objects;

public static class Items
{

    public static readonly EntityBlueprint BaseItem = Primordial.EmbodiedEntity.Derive(
        name: "BaseItem",
        addTags: [TG.Item]
    );

    public static readonly EntityBlueprint Stick = BaseItem.Derive(
        name: "Stick",
        addComponents: () => [
            new TargetComponent(TargetType.Stick),
            new VisualComponent { PendingSpritePath = "res://textures/Stick.png" }
        ]
    );

    // Similar for other items like RawBeef (Food), etc.


    // Steak, etc.
}