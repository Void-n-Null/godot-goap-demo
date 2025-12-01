using System.Collections.Generic;
using Game.Data.Blueprints.Objects;

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
            OutputTag = Tags.Campfire,
            Blueprint = Campfire.SimpleCampfire,
            Ingredients = new Dictionary<Tag, int>
            {
                { Tags.Stick, 2 }
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
            OutputTag = Tags.Bed,
            Blueprint = Furniture.Bed,
            Ingredients = new Dictionary<Tag, int>
            {
                { Tags.Stick, 4 }
            },
            BuildTime = 5.0f,
            SpacingRadius = 200f
        });
    }
}

