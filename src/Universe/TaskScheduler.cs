using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using Game.Utils;

namespace Game.Universe;

public sealed partial class TaskScheduler : SingletonNode<TaskScheduler>, ITaskScheduler
{
	private const int DEFAULT_MAX_TICK_QUEUE = 10000;
	private const int DEFAULT_MAX_TIME_QUEUE = 10000;

	private long _currentTick = 0;
	private double _elapsedSeconds = 0.0;

	private readonly object _lock = new();

	private readonly List<ScheduledItem> _tickQueue = new();
	private readonly List<ScheduledItem> _timeQueue = new();
	private readonly List<ScheduledItem> _executing = new();

	public int MaxTickQueue { get; private set; } = DEFAULT_MAX_TICK_QUEUE;
	public int MaxTimeQueue { get; private set; } = DEFAULT_MAX_TIME_QUEUE;

	public int PendingTickTasks { get { lock (_lock) return _tickQueue.Count; } }
	public int PendingTimeTasks { get { lock (_lock) return _timeQueue.Count; } }

	private sealed class Handle : IScheduledTaskHandle
	{
		private ScheduledItem _item;
		public Handle(ScheduledItem item) { _item = item; }
		public bool IsCanceled => _item.Canceled;
		public bool IsCompleted => _item.Completed;
		public bool Cancel() { return _item.TryCancel(); }
	}

	private sealed class ScheduledItem
	{
		public Action Action;
		public long DueTick;
		public double DueTime;
		public bool UseTick; // true => tick-based, false => time-based
		public CancellationToken CancellationToken;
		public CancellationTokenRegistration Registration;

		public bool Canceled;
		public bool Completed;

		public bool TryCancel()
		{
			if (Completed || Canceled) return false;
			Canceled = true;
			Registration.Dispose();
			return true;
		}
	}

	public override void _Ready()
	{
		base._Ready();
		GameManager.Instance.SubscribeToTick(OnTick);
		GameManager.Instance.SubscribeToFrame(OnFrame);
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (GameManager.HasInstance)
		{
			GameManager.Instance.UnsubscribeFromTick(OnTick);
			GameManager.Instance.UnsubscribeFromFrame(OnFrame);
		}
		lock (_lock)
		{
			foreach (var q in _tickQueue) q.TryCancel();
			foreach (var q in _timeQueue) q.TryCancel();
			_tickQueue.Clear();
			_timeQueue.Clear();
		}
	}

	private void OnFrame(double delta)
	{
		_elapsedSeconds += delta;
	}

	private void OnTick(double _)
	{
		_currentTick++;

		lock (_lock)
		{
			if (_tickQueue.Count > 0)
			{
				for (int i = _tickQueue.Count - 1; i >= 0; i--)
				{
					var item = _tickQueue[i];
					if (item.DueTick <= _currentTick)
					{
						_executing.Add(item);
						_tickQueue.RemoveAt(i);
					}
				}
			}

			if (_timeQueue.Count > 0)
			{
				for (int i = _timeQueue.Count - 1; i >= 0; i--)
				{
					var item = _timeQueue[i];
					if (item.DueTime <= _elapsedSeconds)
					{
						_executing.Add(item);
						_timeQueue.RemoveAt(i);
					}
				}
			}
		}

		for (int i = 0; i < _executing.Count; i++)
		{
			var item = _executing[i];
			if (item.Canceled || item.CancellationToken.IsCancellationRequested)
			{
				item.Registration.Dispose();
				continue;
			}
			try
			{
				item.Action?.Invoke();
			}
			catch (Exception ex)
			{
				GD.PushError($"TaskScheduler: Scheduled action threw: {ex}");
			}
			finally
			{
				item.Completed = true;
				item.Registration.Dispose();
			}
		}
		_executing.Clear();
	}

	public IScheduledTaskHandle ScheduleTicks(Action action, int ticksDelay, CancellationToken cancellationToken = default)
	{
		if (ticksDelay < 1) ticksDelay = 1;
		if (action == null) throw new ArgumentNullException(nameof(action));
		var item = new ScheduledItem
		{
			Action = action,
			UseTick = true,
			DueTick = _currentTick + ticksDelay,
			CancellationToken = cancellationToken
		};
		item.Registration = cancellationToken.Register(() => item.TryCancel());
		lock (_lock)
		{
			if (_tickQueue.Count >= MaxTickQueue)
			{
				GD.PushError("TaskScheduler: Tick queue overflow");
				return new Handle(item);
			}
			_tickQueue.Add(item);
		}
		return new Handle(item);
	}

	public IScheduledTaskHandle ScheduleSeconds(Action action, double secondsDelay, CancellationToken cancellationToken = default)
	{
		if (secondsDelay < 0) secondsDelay = 0;
		if (action == null) throw new ArgumentNullException(nameof(action));
		var item = new ScheduledItem
		{
			Action = action,
			UseTick = false,
			DueTime = _elapsedSeconds + secondsDelay,
			CancellationToken = cancellationToken
		};
		item.Registration = cancellationToken.Register(() => item.TryCancel());
		lock (_lock)
		{
			if (_timeQueue.Count >= MaxTimeQueue)
			{
				GD.PushError("TaskScheduler: Time queue overflow");
				return new Handle(item);
			}
			_timeQueue.Add(item);
		}
		return new Handle(item);
	}
}


