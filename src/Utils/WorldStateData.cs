using System.Collections.Generic;
using Godot;

namespace Game.Utils;

public class WorldStateData
{
	public Dictionary<string, int> EntityCounts { get; } = [];
	public Dictionary<string, bool> AvailabilityFlags { get; } = [];
	public Dictionary<string, Vector2> EntityPositions { get; } = [];

	public WorldStateData() { }
}
