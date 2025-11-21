namespace Game.Utils;

/// <summary>
/// Simple global clock advanced once per frame by GameManager.
/// Avoids repeated calls into Godot's time APIs in hot paths.
/// </summary>
public static class FrameTime
{
    public static float TimeSeconds { get; private set; }
    public static ulong FrameIndex { get; private set; }

    public static void Advance(float deltaSeconds)
    {
        if (deltaSeconds < 0f)
        {
            return;
        }

        TimeSeconds += deltaSeconds;
        FrameIndex++;
    }
}

