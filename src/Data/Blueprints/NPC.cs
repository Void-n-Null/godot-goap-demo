using Game.Data.Components;
using Godot;
using Game.Utils;
namespace Game.Data.Blueprints;


public static class NPC
{
    // Base NPC
    // This is the base NPC that all NPCs should derive from. Has no behavior.
    public static readonly EntityBlueprint NPCBase = Primordial.EmbodiedEntity.Derive(
        name: "NPC",
        addTags: [
            Tags.NPC,
            Tags.Alive,
            Tags.Human
        ],
        addComponents: () => [
            new NPCMotorComponent(maxSpeed: 900f * Random.NextFloat(0.4f, 1.7f), friction: 0.04f, maxAcceleration: 1500f, targetReachedRadius: 32f), //Enables movement
        ],
        addMutators: [
            EntityBlueprint.Mutate<VisualComponent>((c) => c.PendingSpritePath = Random.NextItem([
                "res://textures/Boy.png",
                "res://textures/Female.png",
                "res://textures/Girl.png",
                "res://textures/Male.png"
            ])),
            // EntityBlueprint.Mutate<NPCMotorComponent>((c) => c.DebugDrawEnabled = true)
        ]
    );

    // Follower: Follows the mouse
    public static readonly EntityBlueprint Follower = NPCBase.Derive(
        name: "Follower",
        addComponents: () => [
            new FollowBehavior()
        ]
    );

    public static readonly EntityBlueprint Wanderer = NPCBase.Derive(
        name: "Wanderer",
        addComponents: () => [
            new WanderBehavior(radius: 1000f)
        ]
    );
}


