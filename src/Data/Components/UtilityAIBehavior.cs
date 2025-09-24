using Godot;
using System;
using Game.Data.NPCActions;
using Game.Utils;

namespace Game.Data.Components;

public class UtilityAIBehavior : IActiveComponent
{
    public Entity Entity { get; set; }

	private INPCAction _currentAction;
	private NPCData _npcData;
	private RandomNumberGenerator _rng;

    public void Update(double delta)
    {
		if (_currentAction == null || _currentAction.IsComplete)
		{
			SelectNextAction();
		}

		_currentAction?.OnUpdate(Entity, delta);
    }

    public void OnStart()
    {
		_rng = new RandomNumberGenerator();
		_rng.Randomize();
		_npcData = Entity.GetComponent<NPCData>();
		SelectNextAction();
    }

    public void OnPostAttached()
    {

    }

	private void SelectNextAction()
	{
		_currentAction?.OnStop(Entity);
		_currentAction = null;

		if (_npcData == null)
		{
			_npcData = Entity.GetComponent<NPCData>();
		}

		// Basic heuristic: if hunger above 60% of max, eat; otherwise randomly burn something.
		var hungerRatio = (_npcData != null && _npcData.MaxHunger > 0f) ? _npcData.Hunger / _npcData.MaxHunger : 0f;

		if (_npcData != null && hungerRatio > 0.6f)
		{
			_currentAction = new ConsumeNearestFood();
		}
		else
		{
			// Small randomness to avoid constant burning.
			bool burn = _rng.Randf() > 0.3f;
			_currentAction = burn ? new BurnFlammable() : new ConsumeNearestFood();
		}

		if (_currentAction != null)
		{
			_currentAction.Prepare(Entity);
			if (_currentAction.IsComplete)
			{
				_currentAction.OnStop(Entity);
				_currentAction = null;
			}
			else
			{
				_currentAction.OnStart(Entity);
			}
		}
	}
}