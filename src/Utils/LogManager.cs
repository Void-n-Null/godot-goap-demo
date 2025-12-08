using System.Diagnostics;
using Godot;

namespace Game.Utils;

/// <summary>
/// Centralized logging helper with compile-time elimination.
/// [Conditional] attributes ensure the ENTIRE CALL (including argument evaluation)
/// is stripped when the symbol is not defined. No string allocation overhead.
/// 
/// To enable logging, add to your .csproj:
///   &lt;DefineConstants&gt;GOAP_LOGGING&lt;/DefineConstants&gt;
/// </summary>
public static class LM
{
	/// <summary>
	/// Debug-level logging. Calls are completely eliminated unless GOAP_DEBUG is defined.
	/// </summary>
	[Conditional("GOAP_DEBUG")]
	public static void Debug(string message)
	{
		if (string.IsNullOrEmpty(message)) return;
		GD.Print($"[DEBUG] {message}");
	}

	/// <summary>
	/// Info-level logging. Calls are completely eliminated unless GOAP_LOGGING is defined.
	/// </summary>
	[Conditional("GOAP_LOGGING")]
	public static void Info(string message)
	{
		if (string.IsNullOrEmpty(message)) return;
		GD.Print($"[INFO] {message}");
	}

	/// <summary>
	/// Warning-level logging. Calls are completely eliminated unless GOAP_LOGGING is defined.
	/// </summary>
	[Conditional("GOAP_LOGGING")]
	public static void Warning(string message)
	{
		if (string.IsNullOrEmpty(message)) return;
		GD.PushWarning($"[WARN] {message}");
	}

	/// <summary>
	/// Error-level logging. Always enabled - errors should never be silently swallowed.
	/// </summary>
	public static void Error(string message)
	{
		if (string.IsNullOrEmpty(message)) return;
		GD.PushError($"[ERROR] {message}");
	}
}
