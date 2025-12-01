using Game.Data.Blueprints;
using Game.Data.Components;


namespace Game.Data.Blueprints.Objects;

public static class Food
{
	public static readonly EntityBlueprint BaseFood = Items.BaseItem.Derive(
		name: "BaseFood",
		addTags: [ Tags.Food ],
		addComponents: () => [
			new FoodData()
		]
	);

	public static readonly EntityBlueprint RawBeef = BaseFood.Derive(
		name: "Raw Beef",
		addTags: [ Tags.RawBeef ],
		addMutators: [
			EntityBlueprint.Mutate<VisualComponent>((v) => v.PendingSpritePath = "res://textures/RawBeef.png"),
			EntityBlueprint.Mutate<FoodData>((fd) => {
				fd.CookedVariant = Steak;
				fd.CookTime = 5.0f; // Faster cook time for demo
				fd.HungerRestoredOnConsumption = 10;
			})
		]
	);

	public static readonly EntityBlueprint Steak = BaseFood.Derive(
		name: "Steak",
		addTags: [ Tags.Steak ],
		addMutators: [
			EntityBlueprint.Mutate<VisualComponent>((v) => v.PendingSpritePath = "res://textures/Steak.png"),
			EntityBlueprint.Mutate<FoodData>((fd) => {
				fd.RawVariant = RawBeef;
				fd.IsCooked = true;
				fd.HungerRestoredOnConsumption = 30; // Better value
			})
		]
	);

	// Removed YSorted variant; VisualComponent now renders via CustomEntityRenderEngine
}
