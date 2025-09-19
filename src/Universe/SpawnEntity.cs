using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Game.Data;

namespace Game.Universe;

public static class SpawnEntity
{
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
		TaskScheduler.Instance.ScheduleTicks(() =>
		{
			try { Now(blueprint); }
			catch (Exception ex) { GD.PushError($"SpawnEntity.NextTick threw: {ex}"); }
		}, 1);
	}

	public static void NextTick(EntityBlueprint blueprint, Vector2 position)
	{
		TaskScheduler.Instance.ScheduleTicks(() =>
		{
			try { Now(blueprint, position); }
			catch (Exception ex) { GD.PushError($"SpawnEntity.NextTick threw: {ex}"); }
		}, 1);
	}

	public static Task<Entity> NextTickAsync(EntityBlueprint blueprint, CancellationToken cancellationToken = default)
	{
		var tcs = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskScheduler.Instance.ScheduleTicks(() =>
		{
			try { tcs.TrySetResult(Now(blueprint)); }
			catch (Exception ex) { GD.PushError($"SpawnEntity.NextTickAsync threw: {ex}"); tcs.TrySetException(ex); }
		}, 1, cancellationToken);
		if (cancellationToken.CanBeCanceled) cancellationToken.Register(() => tcs.TrySetCanceled());
		return tcs.Task;
	}

	public static Task<Entity> NextTickAsync(EntityBlueprint blueprint, Vector2 position, CancellationToken cancellationToken = default)
	{
		var tcs = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskScheduler.Instance.ScheduleTicks(() =>
		{
			try { tcs.TrySetResult(Now(blueprint, position)); }
			catch (Exception ex) { GD.PushError($"SpawnEntity.NextTickAsync threw: {ex}"); tcs.TrySetException(ex); }
		}, 1, cancellationToken);
		if (cancellationToken.CanBeCanceled) cancellationToken.Register(() => tcs.TrySetCanceled());
		return tcs.Task;
	}

	public static Task<Entity> AfterTickDelay(EntityBlueprint blueprint, int ticksDelay, CancellationToken cancellationToken = default)
	{
		// Delegate to central scheduler for timing; complete Task with spawned entity
		var tcs = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskScheduler.Instance.ScheduleTicks(() =>
		{
			try
			{
				var e = Now(blueprint);
				tcs.TrySetResult(e);
			}
			catch (Exception ex)
			{
				GD.PushError($"SpawnEntity.ScheduleTicks threw: {ex}");
				tcs.TrySetException(ex);
			}
		}, ticksDelay, cancellationToken);
		if (cancellationToken.CanBeCanceled)
		{
			cancellationToken.Register(() => tcs.TrySetCanceled());
		}
		return tcs.Task;
	}

	public static Task<Entity> AfterTickDelay(EntityBlueprint blueprint, Vector2 position, int ticksDelay, CancellationToken cancellationToken = default)
	{
		var tcs = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskScheduler.Instance.ScheduleTicks(() =>
		{
			try
			{
				var e = Now(blueprint, position);
				tcs.TrySetResult(e);
			}
			catch (Exception ex)
			{
				GD.PushError($"SpawnEntity.ScheduleTicks threw: {ex}");
				tcs.TrySetException(ex);
			}
		}, ticksDelay, cancellationToken);
		if (cancellationToken.CanBeCanceled)
		{
			cancellationToken.Register(() => tcs.TrySetCanceled());
		}
		return tcs.Task;
	}

	public static Task<Entity> AfterSecondDelay(EntityBlueprint blueprint, double secondsDelay, CancellationToken cancellationToken = default)
	{
		var tcs = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskScheduler.Instance.ScheduleSeconds(() =>
		{
			try
			{
				var e = Now(blueprint);
				tcs.TrySetResult(e);
			}
			catch (Exception ex)
			{
				GD.PushError($"SpawnEntity.AfterSecondDelay threw: {ex}");
				tcs.TrySetException(ex);
			}
		}, secondsDelay, cancellationToken);
		if (cancellationToken.CanBeCanceled)
		{
			cancellationToken.Register(() => tcs.TrySetCanceled());
		}
		return tcs.Task;
	}

	public static Task<Entity> AfterSecondDelay(EntityBlueprint blueprint, Vector2 position, double secondsDelay, CancellationToken cancellationToken = default)
	{
		var tcs = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskScheduler.Instance.ScheduleSeconds(() =>
		{
			try
			{
				var e = Now(blueprint, position);
				tcs.TrySetResult(e);
			}
			catch (Exception ex)
			{
				GD.PushError($"SpawnEntity.AfterSecondDelay threw: {ex}");
				tcs.TrySetException(ex);
			}
		}, secondsDelay, cancellationToken);
		if (cancellationToken.CanBeCanceled)
		{
			cancellationToken.Register(() => tcs.TrySetCanceled());
		}
		return tcs.Task;
	}

	// Scheduling is fully delegated to TaskScheduler
}


