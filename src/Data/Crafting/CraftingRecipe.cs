using System.Collections.Generic;
using Game.Data.Components;
using Game.Data.Blueprints;

namespace Game.Data.Crafting;

/// <summary>
/// Defines requirements and output for crafting/building an item.
/// </summary>
public class CraftingRecipe
{
    /// <summary>
    /// Unique name of the recipe step (e.g. "BuildCampfire")
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The TargetType of the resulting entity
    /// </summary>
    public TargetType OutputType { get; set; }

    /// <summary>
    /// The Blueprint to spawn
    /// </summary>
    public EntityBlueprint Blueprint { get; set; }

    /// <summary>
    /// Resources required to build
    /// </summary>
    public Dictionary<TargetType, int> Ingredients { get; set; } = new();

    /// <summary>
    /// Time in seconds to build
    /// </summary>
    public float BuildTime { get; set; } = 3.0f;

    /// <summary>
    /// If > 0, prevents building if another entity of OutputType is within this radius.
    /// </summary>
    public float SpacingRadius { get; set; } = 0f;
}

