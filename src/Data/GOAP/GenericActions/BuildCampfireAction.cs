using System.Linq;
using Game.Data.Components;
using Game.Universe;
using Godot;

namespace Game.Data.GOAP.GenericActions;

/// <summary>
/// Special action to build a campfire at the agent's current location.
/// Doesn't require finding an entity - creates one instead.
/// </summary>
public sealed class BuildCampfireAction : IAction, IRuntimeGuard
{
    private readonly int _sticksRequired;
    private readonly float _buildTime;
    private float _timer;
    private bool _failed;
    private bool _completed;

    public string Name => "BuildCampfire";

    public BuildCampfireAction(int sticksRequired = 16, float buildTime = 3.0f)
    {
        _sticksRequired = sticksRequired;
        _buildTime = buildTime;
    }

    public void Enter(Entity agent)
    {
        _timer = 0f;
        _completed = false;
        _failed = false;

        // Check if agent has enough sticks
        if (!agent.TryGetComponent<NPCData>(out var npcData))
        {
            Fail("Agent lacks NPCData");
            return;
        }

        int currentSticks = npcData.Resources.TryGetValue(TargetType.Stick, out var stickCount) ? stickCount : 0;
        if (currentSticks < _sticksRequired)
        {
            Fail($"Not enough sticks to build campfire! Need {_sticksRequired}, have {currentSticks}");
            return;
        }

        GD.Print($"[{agent.Name}] BuildCampfire: Starting construction (duration: {_buildTime}s, cost: {_sticksRequired} sticks)");
    }

    public ActionStatus Update(Entity agent, float dt)
    {
        if (_failed) return ActionStatus.Failed;
        if (_completed) return ActionStatus.Succeeded;

        _timer += dt;

        if (_timer >= _buildTime)
        {
            if (!agent.TryGetComponent<NPCData>(out var npcData) ||
                !agent.TryGetComponent<TransformComponent2D>(out var transform))
            {
                Fail("Missing required components");
                return ActionStatus.Failed;
            }

            // Double-check sticks (in case they were consumed elsewhere)
            int currentSticks = npcData.Resources.TryGetValue(TargetType.Stick, out var stickCount) ? stickCount : 0;
            if (currentSticks < _sticksRequired)
            {
                Fail($"Lost sticks during construction! Need {_sticksRequired}, have {currentSticks}");
                return ActionStatus.Failed;
            }

            // Deduct sticks
            npcData.Resources[TargetType.Stick] = currentSticks - _sticksRequired;

            // Spawn campfire at agent's position
            var campfire = SpawnEntity.Now(
                Game.Data.Blueprints.Objects.Campfire.SimpleCampfire,
                transform.Position
            );

            _completed = true;
            GD.Print($"Built campfire at {transform.Position}! Sticks remaining: {npcData.Resources[TargetType.Stick]}");
            return ActionStatus.Succeeded;
        }

        return ActionStatus.Running;
    }

    public void Exit(Entity agent, ActionExitReason reason)
    {
        if (reason != ActionExitReason.Completed)
        {
            GD.Print("BuildCampfire canceled before completion");
        }
    }

    public bool StillValid(Entity agent)
    {
        if (_failed) return false;

        // Check if agent still has enough sticks
        if (agent.TryGetComponent<NPCData>(out var npcData))
        {
            int currentSticks = npcData.Resources.TryGetValue(TargetType.Stick, out var stickCount) ? stickCount : 0;
            return currentSticks >= _sticksRequired;
        }

        return false;
    }

    public void Fail(string reason)
    {
        GD.PushError($"BuildCampfire fail: {reason}");
        _failed = true;
    }
}
