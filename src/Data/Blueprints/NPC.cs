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
            new NPCMotorComponent(maxSpeed: 240f * Random.NextFloat(0.4f, 1.7f), friction: 0.04f, maxAcceleration: 1500f, targetReachedRadius: 32f), //Enables movement
            new NPCData()
        ],
        addMutators: [
            EntityBlueprint.Mutate<NPCData>((npc) =>
            {
                npc.Gender = Random.NextEnum<NPCGender>();
                npc.AgeGroup = Random.NextEnum<NPCAgeGroup>();
            }),
            EntityBlueprint.Mutate<VisualComponent>((c, entity) =>
            {
                var npcData = entity.GetComponent<NPCData>();
                c.PendingSpritePath = DetermineSpritePath(npcData);
            }),
            EntityBlueprint.Mutate<VisualComponent>((c) => c.VisualOriginOffset = new Vector2(0,-50f))
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

    public static readonly EntityBlueprint Intelligent = NPCBase.Derive(    
        name: "Intelligent AI NPC",
        addComponents: () => [
            new UtilityGoalSelector(),
            new AIGoalExecutor()
        ],
        addMutators: [
            // Set specific starting stats for demonstration
            EntityBlueprint.Mutate<NPCData>((npc) =>
            {
                npc.Hunger = Random.NextFloat(0f,100f);
                npc.Thirst = 20f;
                npc.Sleepiness = 50f;
            })
        ]
    );
    public static string DetermineSpritePath(NPCData npcData)
    {
        var ageGroup = npcData?.AgeGroup ?? NPCAgeGroup.Adult;
        var gender = npcData?.Gender ?? NPCGender.Male;

        return ageGroup switch
        {
            NPCAgeGroup.Child => gender == NPCGender.Male
                ? "res://textures/Boy.png"
                : "res://textures/Girl.png",
            _ => gender == NPCGender.Male
                ? "res://textures/Male.png"
                : "res://textures/Female.png"
        };
    }
}


