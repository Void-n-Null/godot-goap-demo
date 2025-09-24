using Game.Data;
using Game.Data.Components;
using Game.Data.Blueprints;
using TG = Game.Data.Tags;


public static class Items       
{
    public static readonly EntityBlueprint BaseItem = Primordial.EmbodiedEntity.Derive(
        name: "BaseItem",
        addTags: [ TG.Item ]
    );

    public static readonly EntityBlueprint Stick = BaseItem.Derive(
        name: "Wood",
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = "res://textures/Stick.png")
        ]
    );
}