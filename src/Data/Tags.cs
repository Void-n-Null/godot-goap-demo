namespace Game.Data;

public static class Tags
{

    public static readonly Tag Flammable = Tag.From("Flammable");
    public static readonly Tag AI = Tag.From("AI");
    public static readonly Tag Furniture = Tag.From("Furniture");
    public static readonly Tag CanBeSleptIn = Tag.From("CanBeSleptIn");
    public static readonly Tag NPC = Tag.From("NPC");
    public static readonly Tag Alive = Tag.From("Alive");
    public static readonly Tag Dead = Tag.From("Dead");
    public static readonly Tag Human = Tag.From("Human");
    public static readonly Tag Monster = Tag.From("Monster");
    public static readonly Tag Animal = Tag.From("Animal");
    public static readonly Tag Plant = Tag.From("Plant");
    public static readonly Tag Object = Tag.From("Object");
    public static readonly Tag Item = Tag.From("Item");
    public static readonly Tag Weapon = Tag.From("Weapon");
    public static readonly Tag Food = Tag.From("Food");
    public static readonly Tag Tree = Tag.From("Tree");
    public static readonly Tag Wooden = Tag.From("Wooden");
    public static readonly Tag Campfire = Tag.From("Campfire");
    public static readonly Tag HeatSource = Tag.From("HeatSource");
    
    // Resource/Item target tags (formerly TargetType enum)
    public static readonly Tag Stick = Tag.From("Stick");
    public static readonly Tag Wood = Tag.From("Wood");
    public static readonly Tag Stone = Tag.From("Stone");
    public static readonly Tag RawBeef = Tag.From("RawBeef");
    public static readonly Tag Steak = Tag.From("Steak");
    public static readonly Tag Bed = Tag.From("Bed");
    public static readonly Tag CookingStation = Tag.From("CookingStation");
    
    /// <summary>
    /// All tags that represent targetable/trackable entity types for GOAP planning.
    /// Used by AIGoalExecutor to build world state.
    /// </summary>
    public static readonly Tag[] TargetTags = [
        Stick, Wood, Stone, RawBeef, Steak, Food, Tree, Bed, Campfire
    ];
}


