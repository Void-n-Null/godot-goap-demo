using System.Collections.Generic;
using Game.Data.Blueprints.Objects;
using Game.Data.Components;

namespace Game.Data.Crafting;

public static class CraftingRegistry
{
    public static readonly List<CraftingRecipe> Recipes = new();

    static CraftingRegistry()
    {
        RegisterCampfire();
        RegisterBed();
    }

    private static void RegisterCampfire()
    {
        Recipes.Add(new CraftingRecipe
        {
            Name = "BuildCampfire",
            OutputType = TargetType.Campfire,
            Blueprint = Campfire.SimpleCampfire,
            Ingredients = new Dictionary<TargetType, int>
            {
                { TargetType.Stick, 2 }
            },
            BuildTime = 5.0f,
            SpacingRadius = 1000f
        });
    }

    private static void RegisterBed()
    {
        Recipes.Add(new CraftingRecipe
        {
            Name = "BuildBed",
            OutputType = TargetType.Bed,
            Blueprint = Furniture.Bed,
            Ingredients = new Dictionary<TargetType, int>
            {
                { TargetType.Stick, 4 }
            },
            BuildTime = 5.0f,
            SpacingRadius = 200f
        });
    }
}

