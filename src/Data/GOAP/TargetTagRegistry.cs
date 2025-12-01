using System.Collections.Generic;
using System.Linq;

namespace Game.Data.GOAP;

/// <summary>
/// How an entity behaves spatially in the world
/// </summary>
public enum SpatialBehavior
{
    /// <summary>Fixed in place - campfire, tree, bed. Moving away means NearTarget becomes false.</summary>
    FixedLocation,
    
    /// <summary>Can be picked up and carried - food, sticks, materials</summary>
    Pickupable,
    
    /// <summary>Dropped at a source location - sticks drop from trees, so they're "at" the tree location</summary>
    DroppedAtSource
}

/// <summary>
/// Metadata describing the behavior of a target Tag for GOAP planning
/// </summary>
public class TargetTagMetadata
{
    public Tag Tag { get; init; }
    
    /// <summary>How this target behaves spatially</summary>
    public SpatialBehavior Spatial { get; init; } = SpatialBehavior.Pickupable;
    
    /// <summary>Whether this target requires exclusive reservation (false for shared things like campfire)</summary>
    public bool RequiresExclusiveUse { get; init; } = true;
    
    /// <summary>Whether a PickUp action should be generated for this type</summary>
    public bool CanBePickedUp { get; init; } = true;
    
    /// <summary>Search radius for finding this target (movement)</summary>
    public float MoveSearchRadius { get; init; } = 5000f;
    
    /// <summary>Search radius for interacting with this target</summary>
    public float InteractionRadius { get; init; } = 128f;
    
    /// <summary>Time to pick up this item (if pickupable)</summary>
    public float PickupTime { get; init; } = 0.5f;
    
    /// <summary>Whether moving away from this type should clear NearTarget</summary>
    public bool ClearNearOnMove => Spatial == SpatialBehavior.FixedLocation || Spatial == SpatialBehavior.DroppedAtSource;
}

/// <summary>
/// Defines a harvesting action (e.g., chop tree -> sticks)
/// </summary>
public class HarvestDefinition
{
    public string Name { get; init; }
    public Tag SourceTag { get; init; }
    public Tag ProducedTag { get; init; }
    public int ProducedCount { get; init; } = 1;
    public float InteractionTime { get; init; } = 1.0f;
    public bool DestroySource { get; init; } = true;
    public float Cost { get; init; } = 1.0f;
}

/// <summary>
/// Defines a cooking transformation (e.g., raw beef -> steak)
/// </summary>
public class CookingDefinition
{
    public string DepositStepName { get; init; }
    public string RetrieveStepName { get; init; }
    public Tag InputTag { get; init; }
    public Tag OutputTag { get; init; }
    public Tag StationTag { get; init; }
    public float CookTime { get; init; } = 5.0f;
    public float DepositCost { get; init; } = 1.0f;
    public float RetrieveCost { get; init; } = 10.0f; // Higher to represent waiting
}

/// <summary>
/// Defines what happens when consuming an item
/// </summary>
public class ConsumableDefinition
{
    public string StepName { get; init; }
    public Tag ItemTag { get; init; }
    public string SatisfiesFact { get; init; } // e.g., "IsHungry"
    public bool SatisfiesValue { get; init; } = false; // The value to set (usually false = "not hungry")
    public float Cost { get; init; } = 2.0f;
}

/// <summary>
/// Central registry for all target tag metadata and action definitions.
/// This replaces hardcoded values scattered throughout GenericStepFactory.
/// </summary>
public static class TargetTagRegistry
{
    private static readonly Dictionary<Tag, TargetTagMetadata> _metadata = new();
    private static readonly List<HarvestDefinition> _harvests = new();
    private static readonly List<CookingDefinition> _cooking = new();
    private static readonly List<ConsumableDefinition> _consumables = new();
    
    public static IReadOnlyDictionary<Tag, TargetTagMetadata> Metadata => _metadata;
    public static IReadOnlyList<HarvestDefinition> Harvests => _harvests;
    public static IReadOnlyList<CookingDefinition> Cooking => _cooking;
    public static IReadOnlyList<ConsumableDefinition> Consumables => _consumables;
    
    /// <summary>
    /// All target tags that should clear NearTarget when an agent moves away
    /// </summary>
    public static IEnumerable<Tag> LocationTags => 
        _metadata.Values.Where(m => m.ClearNearOnMove).Select(m => m.Tag);
    
    static TargetTagRegistry()
    {
        RegisterMetadata();
        RegisterHarvests();
        RegisterCooking();
        RegisterConsumables();
    }
    
    private static void RegisterMetadata()
    {
        // Fixed locations - can't be picked up, moving away clears NearTarget
        Register(new TargetTagMetadata
        {
            Tag = Tags.Tree,
            Spatial = SpatialBehavior.FixedLocation,
            CanBePickedUp = false,
            RequiresExclusiveUse = true
        });
        
        Register(new TargetTagMetadata
        {
            Tag = Tags.Campfire,
            Spatial = SpatialBehavior.FixedLocation,
            CanBePickedUp = false,
            RequiresExclusiveUse = false // Campfires can be shared
        });
        
        Register(new TargetTagMetadata
        {
            Tag = Tags.Bed,
            Spatial = SpatialBehavior.FixedLocation,
            CanBePickedUp = false,
            RequiresExclusiveUse = true
        });
        
        // Dropped items - spawned at a location, moving away clears NearTarget
        Register(new TargetTagMetadata
        {
            Tag = Tags.Stick,
            Spatial = SpatialBehavior.DroppedAtSource, // Drops from trees
            CanBePickedUp = true
        });
        
        // Pickupable items - can carry them, NearTarget not relevant after pickup
        Register(new TargetTagMetadata
        {
            Tag = Tags.RawBeef,
            Spatial = SpatialBehavior.Pickupable,
            CanBePickedUp = true
        });
        
        Register(new TargetTagMetadata
        {
            Tag = Tags.Steak,
            Spatial = SpatialBehavior.Pickupable,
            CanBePickedUp = true
        });
        
        Register(new TargetTagMetadata
        {
            Tag = Tags.Food,
            Spatial = SpatialBehavior.Pickupable,
            CanBePickedUp = true
        });
        
        Register(new TargetTagMetadata
        {
            Tag = Tags.Wood,
            Spatial = SpatialBehavior.Pickupable,
            CanBePickedUp = true
        });
        
        Register(new TargetTagMetadata
        {
            Tag = Tags.Stone,
            Spatial = SpatialBehavior.Pickupable,
            CanBePickedUp = true
        });
    }
    
    private static void RegisterHarvests()
    {
        _harvests.Add(new HarvestDefinition
        {
            Name = "ChopTree",
            SourceTag = Tags.Tree,
            ProducedTag = Tags.Stick,
            ProducedCount = 4,
            InteractionTime = 3.0f,
            DestroySource = true,
            Cost = 3.0f
        });
        
        // Add more harvest definitions here (e.g., mine stone, gather berries)
    }
    
    private static void RegisterCooking()
    {
        _cooking.Add(new CookingDefinition
        {
            DepositStepName = "DepositRawBeef",
            RetrieveStepName = "RetrieveSteak",
            InputTag = Tags.RawBeef,
            OutputTag = Tags.Steak,
            StationTag = Tags.Campfire,
            CookTime = 5.0f,
            DepositCost = 1.0f,
            RetrieveCost = 10.0f
        });
    }
    
    private static void RegisterConsumables()
    {
        _consumables.Add(new ConsumableDefinition
        {
            StepName = "EatSteak",
            ItemTag = Tags.Steak,
            SatisfiesFact = "IsHungry",
            SatisfiesValue = false,
            Cost = 1.0f  // Cooked food is more efficient (lower cost = preferred)
        });
        
        // _consumables.Add(new ConsumableDefinition
        // {
        //     StepName = "EatRawBeef",
        //     ItemTag = Tags.RawBeef,
        //     SatisfiesFact = "IsHungry",
        //     SatisfiesValue = false,
        //     Cost = 50.0f  // Raw meat works but is less efficient (higher cost)
        // });
    }
    
    private static void Register(TargetTagMetadata metadata)
    {
        _metadata[metadata.Tag] = metadata;
    }
    
    public static TargetTagMetadata Get(Tag tag)
    {
        if (_metadata.TryGetValue(tag, out var meta))
            return meta;
        
        // Return sensible defaults for unknown tags
        return new TargetTagMetadata
        {
            Tag = tag,
            Spatial = SpatialBehavior.Pickupable,
            CanBePickedUp = true,
            RequiresExclusiveUse = true
        };
    }
    
    public static bool ShouldClearNearOnMove(Tag tag)
    {
        return Get(tag).ClearNearOnMove;
    }
}
