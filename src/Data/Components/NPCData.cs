using System;
using System.Collections.Generic;
using Game.Data.Components;
using Godot;
using Game.Universe;

namespace Game.Data;

public enum NPCGender
{
    Male,
    Female
}

public enum NPCAgeGroup
{
    Child,
    Adult
}

public class NPCData : IActiveComponent
{
    const float DEFAULT_MAX = 100f;
    private const float MatingDesireMax = 100f;
    public const float MateCooldownSeconds = 90f;
    private const float MatingGainPerSecond = 3.5f;
    private const float MatingDecayPerSecond = 10f;
    public const float MatingDesireThreshold = 65f;

    public Dictionary<TargetType, int> Resources { get; } = new Dictionary<TargetType, int>();
    public Entity Entity { get; set; }
    public string Name { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; } = DEFAULT_MAX;
    public float Hunger { get; set; } = 0f;
    public float MaxHunger { get; set; } = 100f;
    public float Thirst { get; set; }
    public float MaxThirst { get; set; } = DEFAULT_MAX;
    public float Sleepiness { get; set; }
    public float MaxSleepiness { get; set; } = DEFAULT_MAX;
    public float Happiness { get; set; }
    public float MaxHappiness { get; set; } = DEFAULT_MAX;
    public float Temperature { get; set; }
    public NPCGender Gender { get; set; } = NPCGender.Male;
    public NPCAgeGroup AgeGroup { get; set; } = NPCAgeGroup.Adult;
    public float MatingDesire { get; set; }
    public double MateCooldownUntil { get; private set; }
    public Guid ActiveMateTargetId { get; private set; } = Guid.Empty;


    public void Update(double delta)
    {
        // Lets look at the world! If were within 200 units of a heat source, we get warmer up to a max of 100, otherwise we get slowly colder!
        var heatSources = Game.Universe.EntityManager.Instance.QueryByTag(Tags.HeatSource, Entity.Transform.Position, 200f);
        foreach (var heatSource in heatSources)
        {
            Temperature += (float)delta * 10f;
        }

        Temperature -= (float)delta * 0.1f;
        Temperature = Mathf.Clamp(Temperature, 0f, 100f);

        // We slowly lose hunger and thirst and sleepiness
        Hunger -= (float)delta * 0.1f;
        Thirst -= (float)delta * 0.1f;
        Sleepiness -= (float)delta * 0.1f;

        Hunger = Mathf.Clamp(Hunger, 0f, MaxHunger);
        Thirst = Mathf.Clamp(Thirst, 0f, MaxThirst);
        Sleepiness = Mathf.Clamp(Sleepiness, 0f, MaxSleepiness);

        UpdateMatingDesire(delta);
    }

    private void UpdateMatingDesire(double delta)
    {
        if (!IsAdult)
        {
            MatingDesire = 0f;
            return;
        }

        float dt = (float)delta;
        if (IsOnMateCooldown)
        {
            MatingDesire = Mathf.Max(0f, MatingDesire - MatingDecayPerSecond * dt);
        }
        else
        {
            MatingDesire = Mathf.Min(MatingDesireMax, MatingDesire + MatingGainPerSecond * dt);
        }
    }

    public bool IsAdult => AgeGroup == NPCAgeGroup.Adult;
    public bool IsMale => Gender == NPCGender.Male;
    public bool IsFemale => Gender == NPCGender.Female;

    public bool IsOnMateCooldown => GameManager.Instance.CachedTimeMsec / 1000.0 <= MateCooldownUntil;
    public double MateSearchCooldownUntil { get; private set; }
    public bool IsOnMateSearchCooldown => GameManager.Instance.CachedTimeMsec / 1000.0 <= MateSearchCooldownUntil;

    public bool ShouldSeekMate => IsAdult && !IsOnMateCooldown && !IsOnMateSearchCooldown && !HasActiveMate && MatingDesire >= MatingDesireThreshold && IsMale;
    public bool HasActiveMate => ActiveMateTargetId != Guid.Empty;
    public bool IsAvailableForMate(Entity requester) => IsAdult && !IsOnMateCooldown && (!HasActiveMate || ActiveMateTargetId == requester?.Id);

    public void ApplyMateCooldown()
    {
        MatingDesire = 0f;
        MateCooldownUntil = GameManager.Instance.CachedTimeMsec / 1000.0 + MateCooldownSeconds;
        ClearActiveMate();
    }

    public void ApplyMateSearchCooldown(float seconds)
    {
        MateSearchCooldownUntil = GameManager.Instance.CachedTimeMsec / 1000.0 + seconds;
    }

    public void ClearMateCooldown()
    {
        MateCooldownUntil = 0;
        MateSearchCooldownUntil = 0;
    }

    public void SetActiveMate(Entity partner)
    {
        ActiveMateTargetId = partner?.Id ?? Guid.Empty;
    }

    public void ClearActiveMate()
    {
        ActiveMateTargetId = Guid.Empty;
    }

    public enum MateRequestStatus
    {
        None,
        Pending,
        Accepted,
        Rejected
    }

    public MateRequestStatus IncomingMateRequestStatus { get; private set; } = MateRequestStatus.None;
    public Guid IncomingMateRequestFrom { get; private set; } = Guid.Empty;

    public void ReceiveMateRequest(Entity from)
    {
        if (from == null) return;
        IncomingMateRequestFrom = from.Id;
        IncomingMateRequestStatus = MateRequestStatus.Pending;

        // CRITICAL: Force immediate goal re-evaluation
        // RespondToMateGoal has 0.95 utility, so it should take precedence
        if (Entity.TryGetComponent<AIGoalExecutor>(out var executor))
        {
            executor.CancelCurrentPlan(); // Cancel whatever we're doing
        }

        if (Entity.TryGetComponent<UtilityGoalSelector>(out var selector))
        {
            selector.ForceReevaluation(); // Immediately pick RespondToMateGoal
        }
    }

    public void AcceptMateRequest()
    {
        IncomingMateRequestStatus = MateRequestStatus.Accepted;
    }

    public void RejectMateRequest()
    {
        IncomingMateRequestStatus = MateRequestStatus.Rejected;
    }

    public void ClearMateRequest()
    {
        IncomingMateRequestStatus = MateRequestStatus.None;
        IncomingMateRequestFrom = Guid.Empty;
    }

    public Entity GetActiveMateEntity()
    {
        if (!HasActiveMate) return null;
        return Game.Universe.EntityManager.Instance.GetEntityById(ActiveMateTargetId);
    }
}