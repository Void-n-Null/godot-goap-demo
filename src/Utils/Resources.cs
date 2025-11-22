using Godot;
using System;
using System.Collections.Concurrent;

namespace Game.Utils;

public static class Resources
{
	private static readonly ConcurrentDictionary<(Type type, string path), Resource> _cache = new();

	private static string NormalizePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Resource path cannot be null or empty.", nameof(path));
		}

		var normalized = path.Replace('\\', '/');
		if (!normalized.StartsWith("res://", StringComparison.Ordinal))
		{
			normalized = "res://" + normalized.TrimStart('/');
		}

		return normalized;
	}

	private static T GetResource<T>(string path) where T : Resource
	{
		var normalized = NormalizePath(path);
		var key = (typeof(T), normalized);

		if (_cache.TryGetValue(key, out var cached) && cached is T typed)
		{
			return typed;
		}

		var resource = GD.Load<T>(normalized);
		if (resource != null)
		{
			_cache[key] = resource;
		}

		return resource;
	}

	public static Texture2D GetTexture(string path) => GetResource<Texture2D>(path);

	public static PackedScene GetScene(string path) => GetResource<PackedScene>(path);

	public static AudioStream GetAudioStream(string path) => GetResource<AudioStream>(path);

	public static AudioStreamMP3 GetAudioStreamMP3(string path) => GetResource<AudioStreamMP3>(path);

	public static void ClearCache() => _cache.Clear();
}