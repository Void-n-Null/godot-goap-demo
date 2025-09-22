using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Game.Data;

namespace Game.Universe;

public static class SpawnEntity
{
	private readonly static EntityManager _entityManager = EntityManager.Instance;

		/// <summary>
	/// Spawns an entity from a blueprint at a given position. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <param name="position">The position to spawn the entity at.</param>
	/// <returns>The spawned entity.</returns>
	public static Entity Now(EntityBlueprint blueprint, Vector2 position)
	{
		var entity = EntityFactory.Create(blueprint);
		_entityManager.RegisterEntity(entity);
		if (entity.Transform != null)
			entity.Transform.Position = position;
		entity.OnStart();
		return entity;
	}

	/// <summary>
	/// Spawns an entity from a blueprint. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <returns>The spawned entity.</returns>
	public static Entity Now(EntityBlueprint blueprint)
	{
		var entity = EntityFactory.Create(blueprint);
		_entityManager.RegisterEntity(entity);
		entity.OnStart();
		return entity;
	}


	private static void ScheduleTicks(Action action, int ticksDelay, string context, CancellationToken cancellationToken = default)
	{
		static void TryRun(Action action, string context)
		{
			try { action(); }
			catch (Exception ex) { GD.PushError($"SpawnEntity.{context} threw: {ex}"); }
		}
		TaskScheduler.Instance.ScheduleTicks(() => TryRun(action, context), ticksDelay, cancellationToken);
	}

	private static Task<Entity> ScheduleTicksAsync(Func<Entity> spawnFunc, int ticksDelay, string context, CancellationToken cancellationToken)
	{
		var tcs = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskScheduler.Instance.ScheduleTicks(() =>
		{
			try { tcs.TrySetResult(spawnFunc()); }
			catch (Exception ex) { GD.PushError($"SpawnEntity.{context} threw: {ex}"); tcs.TrySetException(ex); }
		}, ticksDelay, cancellationToken);
		if (cancellationToken.CanBeCanceled) cancellationToken.Register(() => tcs.TrySetCanceled());
		return tcs.Task;
	}

	private static Task<Entity> ScheduleSecondsAsync(Func<Entity> spawnFunc, double secondsDelay, string context, CancellationToken cancellationToken)
	{
		var tcs = new TaskCompletionSource<Entity>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskScheduler.Instance.ScheduleSeconds(() =>
		{
			try { tcs.TrySetResult(spawnFunc()); }
			catch (Exception ex) { GD.PushError($"SpawnEntity.{context} threw: {ex}"); tcs.TrySetException(ex); }
		}, secondsDelay, cancellationToken);
		if (cancellationToken.CanBeCanceled) cancellationToken.Register(() => tcs.TrySetCanceled());
		return tcs.Task;
	}


	/// <summary>
	/// Spawns an entity at the start of the next tick. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <returns>The spawned entity.</returns>
	public static void NextTick(EntityBlueprint blueprint)
	{
		// Task Scheduler, please create this entity in 1 tick :)
		ScheduleTicks(() => Now(blueprint), 1, "NextTick");
	}

	/// <summary>
	/// Spawns an entity at the start of the next tick at a given position. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <param name="position">The position to spawn the entity at.</param>
	/// <returns>The spawned entity.</returns>
	public static void NextTick(EntityBlueprint blueprint, Vector2 position)
	{
		// Task Scheduler, please create this entity in 1 tick :)
		ScheduleTicks(() => Now(blueprint, position), 1, "NextTick");
	}

	/// <summary>
	/// Spawns an entity at the start of the next tick asynchronously with the ability to cancel. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The spawned entity.</returns>
	public static Task<Entity> NextTickAsync(EntityBlueprint blueprint, CancellationToken cancellationToken = default)
	{
		// Task Scheduler, please create this entity in 1 tick :) (And also let me change my mind later!)
		return ScheduleTicksAsync(() => Now(blueprint), 1, "NextTickAsync", cancellationToken);
	}

	/// <summary>
	/// Spawns an entity at the start of the next tick at a given position asynchronously with the ability to cancel. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <param name="position">The position to spawn the entity at.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The spawned entity.</returns>
	public static Task<Entity> NextTickAsync(EntityBlueprint blueprint, Vector2 position, CancellationToken cancellationToken = default)
	{
		// Task Scheduler, please create this entity in 1 tick :) (And also let me change my mind later!)
		return ScheduleTicksAsync(() => Now(blueprint, position), 1, "NextTickAsync", cancellationToken);
	}

	/// <summary>
	/// Spawns an entity after a given number of ticks with the ability to cancel. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <param name="ticksDelay">The number of ticks to delay.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The spawned entity.</returns>
	public static Task<Entity> AfterTickDelay(EntityBlueprint blueprint, int ticksDelay, CancellationToken cancellationToken = default)
	{
		// Task Scheduler, please create this entity in a couple of ticks :) (And also let me change my mind later!)
		return ScheduleTicksAsync(() => Now(blueprint), ticksDelay, "ScheduleTicks", cancellationToken);
	}

	/// <summary>
	/// Spawns an entity after a given number of ticks at a given position with the ability to cancel. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <param name="position">The position to spawn the entity at.</param>
	/// <param name="ticksDelay">The number of ticks to delay.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The spawned entity.</returns>
	public static Task<Entity> AfterTickDelay(EntityBlueprint blueprint, Vector2 position, int ticksDelay, CancellationToken cancellationToken = default)
	{	
		// Task Scheduler, please create this entity in a couple of ticks :) (And also let me change my mind later!)
		return ScheduleTicksAsync(() => Now(blueprint, position), ticksDelay, "ScheduleTicks", cancellationToken);
	}

	/// <summary>
	/// Spawns an entity after a given number of seconds with the ability to cancel. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <param name="secondsDelay">The number of seconds to delay.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The spawned entity.</returns>
	public static Task<Entity> AfterSecondDelay(EntityBlueprint blueprint, double secondsDelay, CancellationToken cancellationToken = default)
	{
		// Task Scheduler, please create this entity in a couple of seconds :) (And also let me change my mind later!)
		return ScheduleSecondsAsync(() => Now(blueprint), secondsDelay, "AfterSecondDelay", cancellationToken);
	}

	/// <summary>
	/// Spawns an entity after a given number of seconds at a given position with the ability to cancel. Automatically registers the entity to receive updates.
	/// </summary>
	/// <param name="blueprint">The blueprint to spawn the entity from.</param>
	/// <param name="position">The position to spawn the entity at.</param>
	/// <param name="secondsDelay">The number of seconds to delay.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The spawned entity.</returns>
	public static Task<Entity> AfterSecondDelay(EntityBlueprint blueprint, Vector2 position, double secondsDelay, CancellationToken cancellationToken = default)
	{
		// Task Scheduler, please create this entity in a couple of seconds :) (And also let me change my mind later!)
		return ScheduleSecondsAsync(() => Now(blueprint, position), secondsDelay, "AfterSecondDelay", cancellationToken);
	}
}


