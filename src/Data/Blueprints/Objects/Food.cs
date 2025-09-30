using Game.Data.Blueprints;
using Game.Data.Components;


namespace Game.Data.Blueprints.Objects;

public static class Food
{
	public static readonly EntityBlueprint BaseFood = Items.BaseItem.Derive(
		name: "BaseFood",
		addTags: [ Tags.Food ],
		addComponents: () => [
			new FoodData(),
			new TargetComponent(TargetType.Food)
		]

	);

	public static readonly EntityBlueprint RawBeef = BaseFood.Derive(
		name: "Raw Beef",
		addMutators: [
			EntityBlueprint.Mutate<VisualComponent>((v) => v.PendingSpritePath = "res://textures/RawBeef.png"),
			EntityBlueprint.Mutate<FoodData>((fd) => {
				fd.CookedVariant = Steak;
				fd.CookTime = 10f;
				fd.HungerRestoredOnConsumption = 10;
			}),
			EntityBlueprint.Mutate<TargetComponent>(tc => tc.Target = TargetType.Food)
		]
	);

	public static readonly EntityBlueprint Steak = BaseFood.Derive(
		name: "Steak",
		addMutators: [
			EntityBlueprint.Mutate<VisualComponent>((v) => v.PendingSpritePath = "res://textures/Steak.png"),
			EntityBlueprint.Mutate<FoodData>((fd) => {
				fd.RawVariant = RawBeef;
				fd.IsCooked = true;
				fd.HungerRestoredOnConsumption = 10;
			}),
			EntityBlueprint.Mutate<TargetComponent>(tc => tc.Target = TargetType.Food)
		]
	);

	// Removed YSorted variant; VisualComponent now renders via CustomEntityRenderEngine
}
