using System.Collections.Generic;


namespace Game.Utils;

public class WorldStateCache
{
	public Dictionary<string, object> CachedFacts { get; private set; } = [];
	public float CacheTime { get; private set; }

	public WorldStateCache(Dictionary<string, object> facts, float currentTime) {
		CachedFacts = new Dictionary<string, object>(facts);;
		CacheTime = currentTime; 
	}

	public bool IsExpired(float currentTime, float maxAge) =>  currentTime - CacheTime > maxAge;
}