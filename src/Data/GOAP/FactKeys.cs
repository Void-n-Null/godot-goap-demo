namespace Game.Data.GOAP;

/// <summary>
/// Centralized fact key generation to avoid string typos and inconsistencies.
/// Provides clean, predictable fact naming.
/// </summary>
public static class FactKeys
{
    // Agent state facts
    public static string AgentHas(Tag tag) => $"Has_{tag}";
    public static string AgentCount(Tag tag) => $"Agent_{tag}_Count";
    public static string NearTarget(Tag tag) => $"Near_{tag}";

    // World state facts
    public static string WorldHas(Tag tag) => $"World_Has_{tag}";
    public static string WorldCount(Tag tag) => $"World_{tag}_Count";

    // Action state facts
    public static string TargetChopped(Tag tag) => $"{tag}_Chopped";
    
    // Cooking facts
    public static string CampfireCooking(Tag tag) => $"Campfire_Cooking_{tag}";

    // Common facts
    public const string AgentId = "AgentId";
    public const string Position = "Position";
}
