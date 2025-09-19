using System;
using System.Collections.Generic;
using Godot;
using Game.Data;

namespace Game.Universe;

public static class SpawnEntity
{
	private static readonly object _queueLock = new();
	private static readonly List<Action> _nextTick = new();
	private static readonly List<Action> _executing = new();
	private static bool _subscribedToTick = false;

	private static void EnsureSubscribed()
	{
		if (_subscribedToTick) return;
		var gm = GameManager.Instance;
		if (gm != null)
		{
			gm.SubscribeToTick(OnTick);
			_subscribedToTick = true;
		}
	}

	private static void OnTick(double _)
	{
		lock (_queueLock)
		{
			if (_nextTick.Count == 0) return;
			_executing.AddRange(_nextTick);
			_nextTick.Clear();
		}

		for (int i = 0; i < _executing.Count; i++)
		{
			try
			{
				_executing[i].Invoke();
			}
			catch (Exception ex)
			{
				GD.PushError($"SpawnEntity.NextTick action threw: {ex}");
			}
		}
		_executing.Clear();
	}

	public static Entity Now(EntityBlueprint blueprint)
	{
		var entity = EntityManager.Instance.Spawn(blueprint);
		return entity;
	}

	public static Entity Now(EntityBlueprint blueprint, Vector2 position)
	{
		var entity = Now(blueprint);
		if (entity.Transform != null)
			entity.Transform.Position = position;
		return entity;
	}

	public static void NextTick(EntityBlueprint blueprint)
	{
		lock (_queueLock)
			_nextTick.Add(() => Now(blueprint));
		EnsureSubscribed();
	}

	public static void NextTick(EntityBlueprint blueprint, Vector2 position)
	{
		lock (_queueLock)
			_nextTick.Add(() => Now(blueprint, position));
		EnsureSubscribed();
	}
}


