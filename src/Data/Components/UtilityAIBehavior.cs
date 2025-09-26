using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.GOAP;
using Game.Data.Components;
using Game.Universe;

namespace Game.Data.Components;

public class UtilityAIBehavior : IActiveComponent
{
    public Entity Entity { get; set; }

    private Plan _currentPlan;
    private State _goalState;
    private Dictionary<string, object> _trackedFacts = new(); // Local mutable facts for goal checking

    public void Update(double delta)
    {
        var ctx = BuildCurrentState(); // Build state from world/entity

        // Update tracked facts from ctx
        foreach (var fact in ctx.Facts)
        {
            _trackedFacts[fact.Key] = fact.Value;
        }

        if (_currentPlan == null || _currentPlan.IsComplete)
        {
            if (_currentPlan?.Succeeded == true)
            {
                // Celebrate on success
                var woodGoal = new State(new Dictionary<string, object> { {"HasWood", true} });
                if (ctx.Satisfies(woodGoal))
                {
                    GD.Print("Yay! I got wood from chopping a tree! 🎉 Wood acquired!");
                    GD.Print("The forest trembles before my axe skills! 🪓🌲");
                    GD.Print("Time to build something awesome with this wood! 🏠");
                }
                _currentPlan = null;
            }

            // Replan if goal not met
            if (!_HasWood())
            {
                _currentPlan = ForwardPlan(ctx);
                if (_currentPlan != null && _currentPlan.Steps.Count > 0)
                {
                    GD.Print($"Started new plan to get wood: {_currentPlan.Steps.Count} steps");
                }
                else
                {
                    GD.Print("No plan found to get wood, trying again later...");
                }
            }
            else
            {
                GD.Print("Already have wood, relaxing... 😊");
            }
        }

        if (_currentPlan != null && !_currentPlan.IsComplete)
        {
            var tickResult = _currentPlan.Tick(ctx, (float)delta);
            if (tickResult)
            {
                _trackedFacts = new Dictionary<string, object>(_currentPlan.CurrentState.Facts); // Sync effects after step/plan complete
                if (_currentPlan.Succeeded)
                {
                    // Plan done, will celebrate in next update
                }
                else
                {
                    GD.Print("Plan failed, replanning...");
                    _currentPlan = null;
                }
            }
        }
    }

    public void OnStart()
    {
        _goalState = new State(new Dictionary<string, object> { {"HasWood", true} });
        _trackedFacts["NeedsToApproach"] = true; // Initial fact
        GD.Print("UtilityAIBehavior started - goal: acquire wood!");
    }

    public void OnPostAttached()
    {
        // No-op
    }

    private bool _HasWood()
    {
        return _trackedFacts.TryGetValue("HasWood", out var value) && (bool)value;
    }

    private Plan ForwardPlan(State initialState)
    {
        GD.Print($"Planning: Initial state facts: {string.Join(", ", initialState.Facts.Select(kv => $"{kv.Key}={kv.Value}"))}");
        var factories = new List<IStepFactory>
        {
            new GoToStepFactory(),
            new ChopTreeStepFactory()
        };

        var allSteps = factories.SelectMany<IStepFactory, Step>(f => f.CreateSteps(initialState)).ToList();
        GD.Print($"Generated {allSteps.Count} total steps for planning");

        // Forward BFS: start from initial, apply steps to reach goal
        var queue = new Queue<(State state, List<Step> path, int depth)>();
        queue.Enqueue((initialState.Clone(), new List<Step>(), 0));

        var visited = new HashSet<string>();
        int enqueuedCount = 0;
        int dequeuedCount = 0;

        while (queue.Count > 0)
        {
            var (currentState, path, depth) = queue.Dequeue();
            dequeuedCount++;
            var stateHash = string.Join(",", currentState.Facts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
            if (visited.Contains(stateHash)) continue;
            visited.Add(stateHash);

            if (currentState.Satisfies(_goalState))
            {
                GD.Print($"Found plan with {path.Count} steps!");
                return new Plan(path, initialState);
            }

            if (depth > 15) 
            {
                GD.Print($"Skipping deep state (depth {depth})");
                continue; // Limit depth
            }

            var applicableSteps = allSteps.Where(s => s.CanRun(currentState)).ToList();
            GD.Print($"Applicable steps at this state: {applicableSteps.Count}");

            foreach (var step in applicableSteps)
            {
                var newFacts = new Dictionary<string, object>(currentState.Facts);
                foreach (var effect in step.Effects)
                {
                    newFacts[effect.Key] = effect.Value;
                }
                var newState = new State(newFacts);
                var newPath = new List<Step>(path) { step };
                queue.Enqueue((newState, newPath, depth + 1));
                enqueuedCount++;

            }

            if (queue.Count > 200) 
            {
                GD.Print("Queue limit reached, stopping search");
                break; // Limit queue size
            }
        }

        GD.Print($"BFS ended: dequeued {dequeuedCount}, enqueued {enqueuedCount}, visited {visited.Count}. No plan found.");
        return null; // No plan found
    }

    private State BuildCurrentState()
    {
        var facts = new Dictionary<string, object>(_trackedFacts); // Start with tracked
        // Add current position, etc., if needed for actions
        if (Entity.TryGetComponent<TransformComponent2D>(out var transform))
        {
            facts["Position"] = transform.Position;
        }
        facts["AgentId"] = Entity.Id.ToString();
        var state = new State(facts);
        state.Agent = Entity;
        state.World = new World 
        { 
            EntityManager = EntityManager.Instance, 
            GameManager = GameManager.Instance 
        };
        return state;
    }
}