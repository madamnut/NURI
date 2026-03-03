using UnityEngine;

public sealed class ChunkColumnData
{
    // 코너 샘플 크기
    // 셀(16,16,256) 기준 코너는 +1이 필요하므로 (17,257,17)
    public const int SampleSizeX = 17;
    public const int SampleSizeY = 257;
    public const int SampleSizeZ = 17;

    // density amount: 0..128 (byte로 충분)
    private readonly byte[] _density;

    public ChunkColumnData()
    {
        _density = new byte[SampleSizeX * SampleSizeY * SampleSizeZ];
    }

    // (x,y,z) -> 1D 인덱스
    // x가 가장 빨리 변하고, 그 다음 z, 그 다음 y
    public static int Idx(int x, int y, int z)
    {
        return x + SampleSizeX * (z + SampleSizeZ * y);
    }

    public byte Get(int x, int y, int z)
    {
        return _density[Idx(x, y, z)];
    }

    public void Set(int x, int y, int z, byte amount)
    {
        _density[Idx(x, y, z)] = amount;
    }

    public byte[] RawDensity => _density;
}