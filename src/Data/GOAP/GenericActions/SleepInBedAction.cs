using System.Linq;
using Game.Data;
using Game.Data.Components;
using Game.Universe;
using Godot;
using Game.Utils;

namespace Game.Data.GOAP.GenericActions;

/// <summary>
/// Action for an NPC to sleep in a bed.
/// Rotates the visual representation to indicate sleeping.
/// </summary>
public sealed class SleepInBedAction : PeriodicGuardAction
{
    private float _timer;
    private bool _failed;
    private bool _completed;
    private const float SLEEP_DURATION_PER_POINT = 0.1f; // How fast sleepiness decreases
    private bool _postureRestored;

    public override string Name => "SleepInBed";

    public SleepInBedAction() : base(0.5f) // Check validity every 0.5s
    {
    }

    public override void Enter(Entity agent)
    {
        _timer = 0f;
        _completed = false;
        _failed = false;
        _postureRestored = false;

        if (!agent.TryGetComponent<NPCData>(out var npcData))
        {
            Fail("Agent lacks NPCData");
            return;
        }

        // Find nearest bed
        if (!FindAndReserveBed(agent, out var bed))
        {
             Fail("No available bed found nearby");
             return;
        }

        if (!agent.TryGetComponent<TransformComponent2D>(out var transform))
        {
            Fail("Agent lacks TransformComponent2D");
            return;
        }

        // Start sleeping: rotate the sprite sideways
        transform.Rotation = Mathf.Pi / 2; // Rotate 90 degrees

        // Disable motor to prevent moving while sleeping (optional, but good practice)
        if (agent.TryGetComponent<NPCMotorComponent>(out var motor))
        {
            motor.Target = null;
            motor.Velocity = Vector2.Zero;
        }

        LM.Info($"[{agent.Name}] Sleeping in bed...");
    }

    public override ActionStatus Update(Entity agent, float dt)
    {
        if (_failed) return ActionStatus.Failed;
        if (_completed) return ActionStatus.Succeeded;

        _timer += dt;

        if (agent.TryGetComponent<NPCData>(out var npcData))
        {
            // Decrease sleepiness
            npcData.Sleepiness = Mathf.Max(0, npcData.Sleepiness - (dt * 10f)); // Sleep recovers 10 units per second

            // Wake up when well-rested (sleepiness < 5)
            if (npcData.Sleepiness < 5f)
            {
                LM.Info($"[{agent.Name}] Waking up! Sleepiness: {npcData.Sleepiness}");
                RestorePosture(agent);
                _completed = true;
                return ActionStatus.Succeeded;
            }
        }
        else
        {
            Fail("Lost NPCData");
            return ActionStatus.Failed;
        }

        return ActionStatus.Running;
    }

    public override void Exit(Entity agent, ActionExitReason reason)
    {
        LM.Info($"[{agent.Name}] SleepInBedAction.Exit called with reason: {reason}");
        RestorePosture(agent);
    }

    public override bool StillValid(Entity agent)
    {
        if (_failed)
        {
            RestorePosture(agent);
            return false;
        }
        
        // Ensure bed is still there?
        return EvaluateGuardPeriodically(agent, () =>
        {
            return IsBedNearby(agent);
        });
    }

    public override void Fail(string reason)
    {
        LM.Error($"SleepInBed fail: {reason}");
        _failed = true;
    }

    private bool FindAndReserveBed(Entity agent, out Entity bed)
    {
        bed = null;
        if (!agent.TryGetComponent<TransformComponent2D>(out var transform)) return false;

        // We assume we are already near a bed due to preconditions
         var nearby = EntityManager.Instance?.SpatialPartition?.QueryCircle(
            transform.Position,
            128f, // Close range
            e => e.HasTag(Tags.Bed),
            maxResults: 1);

        if (nearby != null && nearby.Count > 0)
        {
            bed = nearby[0];
            return true;
        }
        return false;
    }
    
    private bool IsBedNearby(Entity agent)
    {
        if (!agent.TryGetComponent<TransformComponent2D>(out var transform)) return false;
        
         var nearby = EntityManager.Instance?.SpatialPartition?.QueryCircle(
            transform.Position,
            128f,
            e => e.HasTag(Tags.Bed),
            maxResults: 1);
            
        return nearby != null && nearby.Count > 0;
    }

    private void RestorePosture(Entity agent)
    {
        if (_postureRestored)
        {
            LM.Info($"[{agent.Name}] RestorePosture: Already restored, skipping");
            return;
        }

        _postureRestored = true;

        if (agent.TryGetComponent<TransformComponent2D>(out var transform))
        {
            LM.Info($"[{agent.Name}] RestorePosture: Setting rotation from {transform.Rotation} to 0");
            transform.Rotation = 0f;
            LM.Info($"[{agent.Name}] RestorePosture: Rotation is now {transform.Rotation}");
        }
        else
        {
            LM.Warning($"[{agent.Name}] RestorePosture: No TransformComponent2D found!");
        }

        if (agent.TryGetComponent<NPCMotorComponent>(out var motor))
        {
            motor.Target = null;
            motor.Velocity = Vector2.Zero;
        }
    }
}

