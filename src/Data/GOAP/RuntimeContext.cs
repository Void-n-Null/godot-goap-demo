namespace Game.Data.GOAP;

/// <summary>
/// Runtime execution context containing references to the game world and agent.
/// Used during plan execution, NOT during planning.
/// </summary>
public class RuntimeContext
{
    public Entity Agent { get; set; }
    public World World { get; set; }

    public RuntimeContext(Entity agent, World world)
    {
        Agent = agent;
        World = world;
    }
}
