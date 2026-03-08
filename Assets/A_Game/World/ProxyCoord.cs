using System;

/// <summary>
/// World-aligned proxy region coordinate for render-only LOD1/LOD2 views.
/// X and Z are the starting chunk coordinates of the proxy region.
/// </summary>
public readonly struct ProxyCoord : IEquatable<ProxyCoord>
{
    public readonly int X;
    public readonly int Z;
    public readonly int LodLevel;

    public ProxyCoord(int x, int z, int lodLevel)
    {
        X = x;
        Z = z;
        LodLevel = lodLevel;
    }

    public bool Equals(ProxyCoord other)
    {
        return X == other.X && Z == other.Z && LodLevel == other.LodLevel;
    }

    public override bool Equals(object obj)
    {
        return obj is ProxyCoord other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Z, LodLevel);
    }

    public static bool operator ==(ProxyCoord left, ProxyCoord right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ProxyCoord left, ProxyCoord right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"ProxyCoord(Lod={LodLevel}, X={X}, Z={Z})";
    }
}
