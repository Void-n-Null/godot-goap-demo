using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Game.Utils;

public partial class SingletonNode2D<T> : Node2D where T : SingletonNode2D<T>
{
    private static T _instance;
    public static T Instance {
        get {
            if (_instance == null){
                GD.Print("SingletonNode2D: Instance is not set");
                // Try to find existing instance in scene tree
                if (Engine.GetMainLoop() is SceneTree sceneTree && sceneTree.Root != null) {
                    _instance = FindInstanceInSceneTree(sceneTree.Root);
                    GD.Print("SingletonNode2D: Instance found in scene tree");
                }

                // If still not found, throw exception
                if (_instance == null){
                    GD.Print("SingletonNode2D: Instance could not be found in scene tree");
                    throw new Exception($"Instance of {typeof(T).Name} is not set and could not be found in scene tree");
                }
            }
            GD.Print("SingletonNode2D: Instance is set");
            return _instance;
        }
        private set {
            _instance = value;
        }
    }

    private static T FindInstanceInSceneTree(Node root) {
        // Check if root node itself is the instance
        if (root is T instance) {
            return instance;
        }

        // Recursively search children
        foreach (var child in root.GetChildren()) {
            var found = FindInstanceInSceneTree(child);
            if (found != null) {
                return found;
            }
        }

        return null;
    }

    public override void _Ready(){
        _instance = this as T;
    }
}