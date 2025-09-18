using Godot;
using System;
using System.IO;
using FileAccess = Godot.FileAccess;

namespace Game.Utils;

public static class Resources
{
    private static T GetResource<T>(string path) where T : Resource
    {
        //Do some validation that the path is something godot can actually load
        if (!path.StartsWith("res://"))
        {
            path = "res://" + path;
        }

        return GD.Load<T>(path);
    }

    public static Texture2D GetTexture(string path)
    {
        return GetResource<Texture2D>(path);
    }

    public static PackedScene GetScene(string path)
    {
        return GetResource<PackedScene>(path);
    }

    public static AudioStream GetAudioStream(string path)
    {
        return GetResource<AudioStream>(path);
    }


    public static AudioStreamMP3 GetAudioStreamMP3(string path)
    {
        return GetResource<AudioStreamMP3>(path);
    }
    
    
}