using System;

namespace Game.Data;

public static class Tags
{
    /// <summary>
    /// Lookup table: TargetTagIndexById[tag.Id] = index in TargetTags, or -1 if not a target tag.
    /// Enables O(1) target tag lookups instead of iterating.
    /// </summary>
    public static int[] TargetTagIndexById { get; private set; } = [];
    
    private static bool _initialized;

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

    /// <summary>
    /// Initialize the target tag lookup table. Must be called once at startup.
    /// </summary>
    public static void InitializeTargetTagLookup()
    {
        if (_initialized) return;
        _initialized = true;

        // Find max tag ID
        int maxId = 0;
        foreach (var tag in TargetTags)
        {
            if (tag.Id > maxId) maxId = tag.Id;
        }

        // Build lookup: TargetTagIndexById[tag.Id] = index, or -1
        TargetTagIndexById = new int[maxId + 1];
        Array.Fill(TargetTagIndexById, -1);
        
        for (int i = 0; i < TargetTags.Length; i++)
        {
            TargetTagIndexById[TargetTags[i].Id] = i;
        }
    }

    /// <summary>
    /// Get the target tag index for a given tag ID. Returns -1 if not a target tag.
    /// O(1) lookup after initialization.
    /// </summary>
    public static int GetTargetTagIndex(int tagId)
    {
        var lookup = TargetTagIndexById;
        return (uint)tagId < (uint)lookup.Length ? lookup[tagId] : -1;
    }
}


