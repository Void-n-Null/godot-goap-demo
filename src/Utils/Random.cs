using Godot;
using System;
using System.Collections.Generic;

namespace Game.Utils;

public static class Random
{
    public static System.Random Instance { get; private set; } = new System.Random();

    public static void Initialize(int seed = 0)
    {
        Instance = new System.Random(seed);
    }

    public static Vector2 NextVector2(float min, float max)
    {
        return new Vector2(NextFloat(min, max), NextFloat(min, max));
    }

    public static float NextFloat(float min, float max)
    {
        return Instance.NextSingle() * (max - min) + min;
    }

    public static int NextInt(int min, int max)
    {
        return Instance.Next(min, max);
    }

    public static bool NextBool()
    {
        return Instance.Next(0, 2) == 1;
    }

    public static T NextEnum<T>() where T : struct, Enum
    {
        return (T)Enum.GetValues(typeof(T)).GetValue(Instance.Next(0, Enum.GetValues(typeof(T)).Length));
    }

    public static T NextItem<T>(IList<T> list)
    {
        return list[Instance.Next(0, list.Count)];
    }

    public static T NextItem<T>(T[] array)
    {
        return array[Instance.Next(0, array.Length)];
    }

    public static Vector2 InsideCircle(Vector2 center, float radius)
    {
        var angle = NextFloat(0, 2 * Mathf.Pi);
        var distance = NextFloat(0, radius);
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }
}