using UnityEngine;

/// <summary>
/// 월드 좌표, 청크 좌표, 로컬 좌표, 샘플 인덱스를 서로 변환하는 유틸리티 클래스이다.
///
/// 이 프로젝트에서 가장 중요한 규칙 중 하나는 셀과 샘플을 혼동하지 않는 것이다.
/// 마칭 큐브는 셀 1개를 만들 때 8개의 코너 샘플을 참조하므로,
/// 셀 좌표계와 샘플 좌표계를 항상 분리해서 다뤄야 한다.
/// </summary>
public static class WorldMath
{
    /// <summary>
    /// 월드 샘플 X, Z 좌표로부터 그 샘플을 대표해서 읽을 청크 좌표를 구한다.
    ///
    /// 경계 샘플은 인접 청크와 공유되기 때문에, 조회할 때 사용할 대표 청크 규칙이 필요하다.
    /// 이 메서드는 floor division을 사용해 그 대표 청크를 정한다.
    /// </summary>
    public static ChunkCoord WorldSampleToChunkCoord(int worldSampleX, int worldSampleZ)
    {
        int chunkX = FloorDiv(worldSampleX, WorldConstants.ChunkSizeX);
        int chunkZ = FloorDiv(worldSampleZ, WorldConstants.ChunkSizeZ);
        return new ChunkCoord(chunkX, chunkZ);
    }

    /// <summary>
    /// 월드 샘플 X, Z 좌표를 청크 내부 로컬 샘플 X, Z 좌표로 변환한다.
    /// 반환 범위는 0..16이다.
    /// </summary>
    public static Vector2Int WorldSampleToLocalSampleXZ(int worldSampleX, int worldSampleZ)
    {
        ChunkCoord chunkCoord = WorldSampleToChunkCoord(worldSampleX, worldSampleZ);
        int localSampleX = worldSampleX - chunkCoord.X * WorldConstants.ChunkSizeX;
        int localSampleZ = worldSampleZ - chunkCoord.Z * WorldConstants.ChunkSizeZ;
        return new Vector2Int(localSampleX, localSampleZ);
    }

    /// <summary>
    /// 월드 셀 X, Z 좌표로부터 그 셀이 속한 청크 좌표를 구한다.
    /// </summary>
    public static ChunkCoord WorldCellToChunkCoord(int worldX, int worldZ)
    {
        int chunkX = FloorDiv(worldX, WorldConstants.ChunkSizeX);
        int chunkZ = FloorDiv(worldZ, WorldConstants.ChunkSizeZ);
        return new ChunkCoord(chunkX, chunkZ);
    }

    /// <summary>
    /// 월드 셀 좌표를 청크 내부 로컬 셀 좌표로 변환한다.
    /// </summary>
    public static Vector3Int WorldCellToLocalCell(int worldX, int worldY, int worldZ)
    {
        int localX = PositiveMod(worldX, WorldConstants.ChunkSizeX);
        int localZ = PositiveMod(worldZ, WorldConstants.ChunkSizeZ);
        return new Vector3Int(localX, worldY, localZ);
    }

    /// <summary>
    /// 청크 좌표와 로컬 샘플 좌표를 월드 샘플 좌표로 변환한다.
    /// </summary>
    public static Vector3Int LocalSampleToWorldSample(ChunkCoord chunkCoord, int sampleX, int sampleY, int sampleZ)
    {
        int worldX = chunkCoord.X * WorldConstants.ChunkSizeX + sampleX;
        int worldZ = chunkCoord.Z * WorldConstants.ChunkSizeZ + sampleZ;
        return new Vector3Int(worldX, sampleY, worldZ);
    }

    /// <summary>
    /// 3차원 샘플 좌표를 1차원 NativeArray 인덱스로 변환한다.
    ///
    /// 메모리 레이아웃은 X가 가장 빠르고, 그다음 Z, 마지막이 Y이다.
    /// </summary>
    public static int SampleIndex(int sampleX, int sampleY, int sampleZ)
    {
        return sampleX
            + WorldConstants.SampleSizeX * (sampleZ + WorldConstants.SampleSizeZ * sampleY);
    }

    /// <summary>
    /// 3차원 cell 좌표를 1차원 NativeArray 인덱스로 변환한다.
    /// </summary>
    public static int CellIndex(int cellX, int cellY, int cellZ)
    {
        return cellX
            + WorldConstants.ChunkSizeX * (cellZ + WorldConstants.ChunkSizeZ * cellY);
    }

    /// <summary>
    /// 서브청크 인덱스로부터 그 서브청크의 시작 셀 Y를 구한다.
    /// </summary>
    public static int SubChunkStartY(int subChunkIndex)
    {
        return subChunkIndex * WorldConstants.SubChunkSize;
    }

    /// <summary>
    /// 서브청크 인덱스로부터 그 서브청크의 마지막 셀 Y를 구한다.
    /// </summary>
    public static int SubChunkEndY(int subChunkIndex)
    {
        return SubChunkStartY(subChunkIndex) + WorldConstants.SubChunkSize - 1;
    }

    /// <summary>
    /// 셀 Y 좌표가 속한 서브청크 인덱스를 구한다.
    /// </summary>
    public static int CellYToSubChunkIndex(int cellY)
    {
        return cellY / WorldConstants.SubChunkSize;
    }

    /// <summary>
    /// 청크 좌표로부터 그 청크의 월드 원점을 구한다.
    /// </summary>
    public static Vector3 ChunkOrigin(ChunkCoord chunkCoord)
    {
        return new Vector3(
            chunkCoord.X * WorldConstants.ChunkSizeX,
            0f,
            chunkCoord.Z * WorldConstants.ChunkSizeZ);
    }

    /// <summary>
    /// 수학적인 floor division을 수행한다.
    /// 음수 좌표에서도 청크 계산이 안정적으로 맞도록 하기 위해 사용한다.
    /// </summary>
    public static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder != 0 && value < 0)
        {
            quotient--;
        }

        return quotient;
    }

    /// <summary>
    /// 항상 0 이상인 나머지를 반환한다.
    /// </summary>
    public static int PositiveMod(int value, int divisor)
    {
        int remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    public static int AlignDown(int value, int alignment)
    {
        return FloorDiv(value, alignment) * alignment;
    }

    public static int AlignUp(int value, int alignment)
    {
        return FloorDiv(value + alignment - 1, alignment) * alignment;
    }
}
