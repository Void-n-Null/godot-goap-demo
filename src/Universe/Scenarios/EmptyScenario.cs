namespace Game.Universe.Scenarios;

/// <summary>
/// An empty world with no entities - useful for testing or manual setup
/// </summary>
public class EmptyScenario : Scenario
{
    public override string Name => "Empty";
    public override string Description => "Empty world with no entities";
    public override string Entities => "None";

    public override void Setup()
    {
        Log("Setting up empty scenario - no entities spawned");
    }
}
