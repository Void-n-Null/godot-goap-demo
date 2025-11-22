using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;

namespace Game.Data;

/// <summary>
/// Generates entity IDs with a hybrid strategy: random GUIDs for early entities,
/// then fast sequential IDs once spawn counts are high to avoid RNG overhead.
/// </summary>
internal static class EntityIdGenerator
{
	private const int RandomGuidThreshold = 10_000;
	private static int _generated;
	private static long _sequentialCounter;
	private static readonly byte[] _entropy = Guid.NewGuid().ToByteArray();

	public static Guid Next()
	{
		int order = Interlocked.Increment(ref _generated);
		if (order <= RandomGuidThreshold)
		{
			return Guid.NewGuid();
		}

		Span<byte> buffer = stackalloc byte[16];
		long timestamp = Stopwatch.GetTimestamp();
		long sequence = Interlocked.Increment(ref _sequentialCounter);

		BinaryPrimitives.WriteInt64LittleEndian(buffer, timestamp);
		BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(8), sequence);

		// Mix in entropy captured at startup to avoid collisions across sessions.
		for (int i = 0; i < buffer.Length; i++)
		{
			buffer[i] ^= _entropy[i];
		}

		return new Guid(buffer);
	}
}

