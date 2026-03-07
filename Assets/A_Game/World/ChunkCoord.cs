using System;

/// <summary>
/// 월드 안에서 청크를 식별하기 위한 좌표 구조체이다.
///
/// 이 프로젝트는 XZ 평면 기준으로 청크를 나누며,
/// Y축은 청크 내부의 높이 축으로 고정한다.
/// 따라서 청크 좌표는 (X, Z) 두 값만 있으면 충분하다.
///
/// 이 타입은 딕셔너리 키로도 사용할 예정이므로,
/// 값 비교와 해시 코드를 직접 제공한다.
/// </summary>
public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    /// <summary>
    /// X축 청크 좌표이다.
    /// </summary>
    public readonly int X;

    /// <summary>
    /// Z축 청크 좌표이다.
    /// </summary>
    public readonly int Z;

    /// <summary>
    /// 청크 좌표를 생성한다.
    /// </summary>
    public ChunkCoord(int x, int z)
    {
        X = x;
        Z = z;
    }

    /// <summary>
    /// 두 청크 좌표가 완전히 같은 위치를 가리키는지 비교한다.
    /// </summary>
    public bool Equals(ChunkCoord other)
    {
        return X == other.X && Z == other.Z;
    }

    /// <summary>
    /// 박싱된 객체와 비교할 때 사용된다.
    /// </summary>
    public override bool Equals(object obj)
    {
        return obj is ChunkCoord other && Equals(other);
    }

    /// <summary>
    /// 딕셔너리 키로 사용할 수 있도록 해시 코드를 반환한다.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(X, Z);
    }

    public static bool operator ==(ChunkCoord left, ChunkCoord right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ChunkCoord left, ChunkCoord right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// 디버깅할 때 읽기 쉽도록 문자열 형태로 변환한다.
    /// </summary>
    public override string ToString()
    {
        return $"ChunkCoord({X}, {Z})";
    }
}
