using Godot;

namespace Game.Utils;

/// <summary>
/// Centralized logging helper that can be compile-time disabled per level.
/// Flip the constants below to silence specific log levels.
/// </summary>
public static class LM
{
	private const bool LOGGING_ENABLED = true;
	private const bool DEBUG_ENABLED = LOGGING_ENABLED && false;
	private const bool INFO_ENABLED = LOGGING_ENABLED && false;
	private const bool WARNING_ENABLED = LOGGING_ENABLED && true;
	private const bool ERROR_ENABLED = LOGGING_ENABLED && true;

	public static void Debug(string message)
	{
		if (!DEBUG_ENABLED || string.IsNullOrEmpty(message))
			return;

		GD.Print($"[DEBUG] {message}");
	}

	public static void Info(string message)
	{
		if (!INFO_ENABLED || string.IsNullOrEmpty(message))
			return;

		GD.Print($"[INFO] {message}");
	}

	public static void Warning(string message)
	{
		if (!WARNING_ENABLED || string.IsNullOrEmpty(message))
			return;

		GD.PushWarning($"[WARN] {message}");
	}

	public static void Error(string message)
	{
		if (!ERROR_ENABLED || string.IsNullOrEmpty(message))
			return;

		GD.PushError($"[ERROR] {message}");
	}
}
