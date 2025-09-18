using System;

namespace Game.Data;

public readonly struct Tag : IEquatable<Tag>
{
    private readonly int _id; // 0 = invalid

    internal Tag(int id)
    {
        _id = id;
    }

    public static Tag From(string name)
    {
        return new Tag(TagRegistry.GetOrCreateId(name));
    }

    public static bool TryFrom(string name, out Tag tag)
    {
        if (TagRegistry.TryGetId(name, out var id))
        {
            tag = new Tag(id);
            return true;
        }
        tag = default;
        return false;
    }

    public bool IsValid => _id != 0;

    public override string ToString()
    {
        return TagRegistry.GetName(_id);
    }

    public bool Equals(Tag other) => _id == other._id;

    public override bool Equals(object obj) => obj is Tag other && Equals(other);

    public override int GetHashCode() => _id;

    public static bool operator ==(Tag left, Tag right) => left._id == right._id;

    public static bool operator !=(Tag left, Tag right) => left._id != right._id;
}


