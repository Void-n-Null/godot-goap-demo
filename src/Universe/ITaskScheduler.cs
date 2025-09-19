using System;
using System.Threading;

namespace Game.Universe;

public interface IScheduledTaskHandle
{
	bool IsCanceled { get; }
	bool IsCompleted { get; }
	bool Cancel();
}

public interface ITaskScheduler
{
	IScheduledTaskHandle ScheduleTicks(Action action, int ticksDelay, CancellationToken cancellationToken = default);
	IScheduledTaskHandle ScheduleSeconds(Action action, double secondsDelay, CancellationToken cancellationToken = default);

	int PendingTickTasks { get; }
	int PendingTimeTasks { get; }
	int MaxTickQueue { get; }
	int MaxTimeQueue { get; }
}


