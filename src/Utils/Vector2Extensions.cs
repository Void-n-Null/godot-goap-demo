using Godot;

namespace Game.Utils;

public static class Vector2Extensions
{
    public static Vector2 ClampLength(this Vector2 v, float max, float? min = null)
    {
        var len = v.Length();
        
        if (min.HasValue && len <= min.Value)
        {
            if (len == 0) return Vector2.Zero;
            return v / len * min.Value;
        }
        
        if (len >= max)
        {
            return v / len * max;
        }
        
        return v;
    }

    public static Vector2 ClampMaxLength(this Vector2 v, float max)
    {
        var len = v.Length();
        if (len <= max) return v;
        return v / len * max;
    }
}