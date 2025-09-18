using Godot;

namespace Game.Data.Components.Specs;

/// <summary>
/// Editor-serializable spec for building a runtime component.
/// </summary>
[GlobalClass]
public abstract partial class ComponentSpec : Resource
{
    public abstract IComponent CreateComponent();
}


