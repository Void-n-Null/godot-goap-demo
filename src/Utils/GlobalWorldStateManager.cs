using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Game.Data.GOAP;

namespace Game.Utils;

/// <summary>
/// Lock-free global fact store backed by atomic arrays.
/// Agents convert fact names to IDs (via <see cref="FactRegistry"/>) and then read/write
/// <see cref="FactValue"/> entries using <see cref="Interlocked"/> instead of a global lock.
/// </summary>
public sealed class GlobalWorldStateManager
{
	private const int DEFAULT_CAPACITY = 64;

	private static readonly GlobalWorldStateManager _instance = new();
	public static GlobalWorldStateManager Instance => _instance;

	private readonly object _resizeLock = new();
	private long[] _factValues = new long[DEFAULT_CAPACITY];
	private int[] _factPresence = new int[DEFAULT_CAPACITY];
	private long _version;

	static GlobalWorldStateManager()
	{
		int factSize = Unsafe.SizeOf<FactValue>();
		if (factSize > sizeof(long))
		{
			throw new InvalidOperationException(
				$"FactValue must be <= {sizeof(long)} bytes (currently {factSize}) for atomic storage.");
		}
	}

	private GlobalWorldStateManager() { }

	/// <summary>
	/// Monotonic counter that bumps whenever a fact changes (or <see cref="ForceRefresh"/> is invoked).
	/// Callers can cache the version to detect updates without taking locks.
	/// </summary>
	public long Version => Volatile.Read(ref _version);

	/// <summary>
	/// Increments the global version, allowing agents to invalidate their caches without mutating any fact.
	/// </summary>
	public void ForceRefresh() => Interlocked.Increment(ref _version);

	public void SetFact(string factName, FactValue value)
	{
		if (string.IsNullOrEmpty(factName))
		{
			throw new ArgumentException("Fact name cannot be null or empty.", nameof(factName));
		}

		int id = FactRegistry.GetId(factName);
		SetFact(id, value);
	}

	public void SetFact(int factId, FactValue value)
	{
		if (factId < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(factId));
		}

		EnsureCapacity(factId);

		long packed = Pack(value);
		Interlocked.Exchange(ref _factValues[factId], packed);
		Volatile.Write(ref _factPresence[factId], 1);
		Interlocked.Increment(ref _version);
	}

	public bool TryGetFact(string factName, out FactValue value)
	{
		if (string.IsNullOrEmpty(factName))
		{
			value = default;
			return false;
		}

		int id = FactRegistry.GetId(factName);
		return TryGetFact(id, out value);
	}

	public bool TryGetFact(int factId, out FactValue value)
	{
		if (factId < 0 || factId >= _factPresence.Length || Volatile.Read(ref _factPresence[factId]) == 0)
		{
			value = default;
			return false;
		}

		long packed = Volatile.Read(ref _factValues[factId]);
		value = Unpack(packed);
		return true;
	}

	public bool ClearFact(string factName)
	{
		if (string.IsNullOrEmpty(factName))
		{
			return false;
		}

		int id = FactRegistry.GetId(factName);
		return ClearFact(id);
	}

	public bool ClearFact(int factId)
	{
		if (factId < 0 || factId >= _factPresence.Length)
		{
			return false;
		}

		if (Interlocked.Exchange(ref _factPresence[factId], 0) == 0)
		{
			return false;
		}

		Interlocked.Exchange(ref _factValues[factId], 0);
		Interlocked.Increment(ref _version);
		return true;
	}

	/// <summary>
	/// Copies all currently set facts into a <see cref="State"/> snapshot without allocating dictionaries.
	/// </summary>
	public State Snapshot(State destination = null)
	{
		var snapshot = destination ?? new State();
		snapshot.Clear();

		int length = Math.Min(_factValues.Length, _factPresence.Length);
		for (int i = 0; i < length; i++)
		{
			if (Volatile.Read(ref _factPresence[i]) == 0) continue;
			long packed = Volatile.Read(ref _factValues[i]);
			snapshot.Set(i, Unpack(packed));
		}

		return snapshot;
	}

	public void ClearAll()
	{
		Array.Clear(_factValues, 0, _factValues.Length);
		Array.Clear(_factPresence, 0, _factPresence.Length);
		Volatile.Write(ref _version, 0);
	}

	private void EnsureCapacity(int factId)
	{
		if (factId < _factValues.Length)
		{
			return;
		}

		lock (_resizeLock)
		{
			if (factId < _factValues.Length)
			{
				return;
			}

			int newSize = Math.Max(factId + 1, _factValues.Length * 2);
			Array.Resize(ref _factValues, newSize);
			Array.Resize(ref _factPresence, newSize);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long Pack(FactValue value)
	{
		return MemoryMarshal.Cast<FactValue, long>(MemoryMarshal.CreateSpan(ref value, 1))[0];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static FactValue Unpack(long raw)
	{
		return MemoryMarshal.Cast<long, FactValue>(MemoryMarshal.CreateSpan(ref raw, 1))[0];
	}
}

