using System;
using Game.Data;
using Game.Data.Components;

namespace Game.Data.Components;

public enum TargetType
{
    Stick,
    Wood,
    Food,
    Stone,
    Tree,  // For chopping
    Bed,   // For sleeping/resting
    Campfire,  // For warmth and cooking
    // Add more interactables as needed, e.g., Fire for burning, NPC for following
}

public class TargetComponent(TargetType targetType = TargetType.Stick) : IComponent
{
    public TargetType Target { get; set; } = TargetType.Stick;

    public Entity Entity { get; set; }

    public void OnPreAttached()
    {
        Target = targetType;
    }
}

public enum ResourceType
{
    Stick,
    Wood,
    Food,
    Stone
    // Add more as needed
}