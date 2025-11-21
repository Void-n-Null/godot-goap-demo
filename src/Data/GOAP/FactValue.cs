using System;
using System.Runtime.InteropServices;

namespace Game.Data.GOAP;

[StructLayout(LayoutKind.Explicit)]
public struct FactValue
{
    [FieldOffset(0)] public int IntValue;
    [FieldOffset(0)] public float FloatValue;
    [FieldOffset(0)] public bool BoolValue; // Stored as 0 or 1 in the int
    
    // Tag to know what type we are
    [FieldOffset(4)] public FactType Type;

    public static implicit operator FactValue(bool v) => new() { BoolValue = v, Type = FactType.Bool };
    public static implicit operator FactValue(int v) => new() { IntValue = v, Type = FactType.Int };
    public static implicit operator FactValue(float v) => new() { FloatValue = v, Type = FactType.Float };
    
    // Strict equality check: Types MUST match
    public bool Equals(FactValue other)
    {
        if (Type != other.Type) return false;
        return IntValue == other.IntValue;
    }

    // Override standard Equals/GetHashCode for completeness
    public override bool Equals(object obj) => obj is FactValue other && Equals(other);
    public override int GetHashCode() => System.HashCode.Combine(IntValue, Type);
    public static bool operator ==(FactValue left, FactValue right) => left.Equals(right);
    public static bool operator !=(FactValue left, FactValue right) => !left.Equals(right);

    public static FactValue From(object value)
    {
        return value switch
        {
            null => default,
            FactValue fv => fv,
            bool b => b,
            int i => i,
            float f => f,
            double d => (float)d,
            _ => throw new ArgumentException($"Unsupported fact value type {value.GetType().Name}")
        };
    }
}

public enum FactType : byte { Bool, Int, Float }
