using Game.Data.Components;

namespace Game.Data.GOAP;

/// <summary>
/// Centralized fact key generation to avoid string typos and inconsistencies.
/// Provides clean, predictable fact naming.
/// </summary>
public static class FactKeys
{
    // Agent state facts
    public static string AgentHas(TargetType resource) => $"Has_{resource}";
    public static string AgentCount(TargetType resource) => $"Agent_{resource}_Count";
    public static string NearTarget(TargetType target) => $"Near_{target}";
    
    // World state facts
    public static string WorldHas(TargetType resource) => $"World_Has_{resource}";
    public static string WorldCount(TargetType resource) => $"World_{resource}_Count";
    
    // Action state facts
    public static string TargetChopped(TargetType target) => $"{target}_Chopped";

    // Social facts
    public const string MateDesireSatisfied = "MateDesireSatisfied";
    public const string NearMate = "NearMate";
    
    // Common facts
    public const string AgentId = "AgentId";
    public const string Position = "Position";
}
