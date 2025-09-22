using Godot;
using Game.Data.Components;
using Game.Data;
using System;
using Game.Utils;


namespace Game.Data.Components;


//The entity shall blindly follow the mouse via its NPCMotorComponent
public class FollowBehavior : IActiveComponent
{
    public Entity Entity { get; set; }
    public NPCMotorComponent NPCMotorComponent { get; set; }

    public void OnPostAttached()
    {
        NPCMotorComponent = this.GetComponent<NPCMotorComponent>();
    }

    public void Update(double delta)
    {
        NPCMotorComponent.Target = ViewContext.CachedMouseGlobalPosition;
    }
}