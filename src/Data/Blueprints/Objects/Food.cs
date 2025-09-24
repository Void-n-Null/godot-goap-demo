using Game.Data.Blueprints;
using Game.Data.Components;
using Game.Utils;

namespace Game.Data.Blueprints.Objects;

public static class Food
{
    public static readonly EntityBlueprint BaseFood = Primordial.EmbodiedEntity.Derive(
        name: "BaseFood",
        addTags: [ Tags.Food ]
    );

    public static readonly EntityBlueprint RawBeef = BaseFood.Derive(
        name: "Raw Beef",
        addComponents: () => [
            new FoodData(){
                CookedVariant = Steak,
                CookTime = 10f,
                HungerRestoredOnConsumption = 10,
            }
        ],
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = "res://textures/RawBeef.png")
        ]
    );

    public static readonly EntityBlueprint Steak = BaseFood.Derive(
        name: "Steak",
        addComponents: () => [
            new FoodData(){
                RawVariant = RawBeef,
                IsCooked = true,
                HungerRestoredOnConsumption = 10,
            }
        ],
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = "res://textures/Steak.png")
        ]
    );

    // Removed YSorted variant; VisualComponent now renders via CustomEntityRenderEngine
}