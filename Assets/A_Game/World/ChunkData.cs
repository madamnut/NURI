using System;
using Unity.Collections;

/// <summary>
/// 청크 하나의 실제 density 데이터를 보관하는 클래스이다.
///
/// 이 클래스는 렌더링 오브젝트를 관리하지 않고,
/// 순수하게 "청크 데이터"만 소유한다.
///
/// density는 셀 데이터가 아니라 샘플 데이터이며,
/// 마칭 큐브는 이 샘플 값을 읽어 표면을 생성한다.
/// </summary>
public sealed class ChunkData : IDisposable
{
    /// <summary>
    /// 이 데이터가 어느 청크를 나타내는지 식별하는 좌표이다.
    /// </summary>
    public ChunkCoord Coord;

    /// <summary>
    /// 청크 전체 density 샘플을 담는 1차원 배열이다.
    /// 실제 크기는 17 x 257 x 17이며, 인덱스 계산은 WorldMath.SampleIndex를 사용한다.
    /// </summary>
    public NativeArray<byte> Density;

    /// <summary>
    /// 각 cell의 대표 material id를 담는 1차원 배열이다.
    /// 실제 크기는 16 x 256 x 16이고, 인덱스 계산은 WorldMath.CellIndex를 사용한다.
    /// </summary>
    public NativeArray<byte> MaterialIds;

    /// <summary>
    /// 다시 메시가 필요한 서브청크를 비트마스크로 추적한다.
    /// </summary>
    public ushort DirtySubChunkMask;

    /// <summary>
    /// 청크 데이터를 생성하고 density 배열을 Persistent 할당으로 준비한다.
    /// </summary>
    public ChunkData(ChunkCoord coord)
    {
        Coord = coord;
        Density = new NativeArray<byte>(WorldConstants.ChunkSampleCount, Allocator.Persistent);
        MaterialIds = new NativeArray<byte>(WorldConstants.ChunkCellCount, Allocator.Persistent);
        DirtySubChunkMask = 0;
    }

    /// <summary>
    /// density 배열이 유효하게 생성되어 있는지 반환한다.
    /// </summary>
    public bool IsCreated => Density.IsCreated && MaterialIds.IsCreated;

    /// <summary>
    /// 특정 서브청크를 dirty 상태로 표시한다.
    /// </summary>
    public void MarkSubChunkDirty(int subChunkIndex)
    {
        DirtySubChunkMask |= (ushort)(1 << subChunkIndex);
    }

    /// <summary>
    /// 모든 서브청크를 dirty 상태로 표시한다.
    /// </summary>
    public void MarkAllSubChunksDirty()
    {
        DirtySubChunkMask = ushort.MaxValue;
    }

    /// <summary>
    /// 특정 서브청크가 dirty 상태인지 확인한다.
    /// </summary>
    public bool IsSubChunkDirty(int subChunkIndex)
    {
        return (DirtySubChunkMask & (1 << subChunkIndex)) != 0;
    }

    /// <summary>
    /// 특정 서브청크의 dirty 상태를 해제한다.
    /// </summary>
    public void ClearSubChunkDirty(int subChunkIndex)
    {
        DirtySubChunkMask &= (ushort)~(1 << subChunkIndex);
    }

    /// <summary>
    /// density 샘플 값을 읽는다.
    /// </summary>
    public byte GetDensity(int sampleX, int sampleY, int sampleZ)
    {
        return Density[WorldMath.SampleIndex(sampleX, sampleY, sampleZ)];
    }

    /// <summary>
    /// density 샘플 값을 기록한다.
    /// </summary>
    public void SetDensity(int sampleX, int sampleY, int sampleZ, byte value)
    {
        Density[WorldMath.SampleIndex(sampleX, sampleY, sampleZ)] = value;
    }

    /// <summary>
    /// cell 대표 material id를 읽는다.
    /// </summary>
    public byte GetMaterialId(int cellX, int cellY, int cellZ)
    {
        return MaterialIds[WorldMath.CellIndex(cellX, cellY, cellZ)];
    }

    /// <summary>
    /// cell 대표 material id를 기록한다.
    /// </summary>
    public void SetMaterialId(int cellX, int cellY, int cellZ, byte value)
    {
        MaterialIds[WorldMath.CellIndex(cellX, cellY, cellZ)] = value;
    }

    /// <summary>
    /// 청크가 월드에서 제거될 때 NativeArray 메모리를 해제한다.
    /// </summary>
    public void Dispose()
    {
        if (Density.IsCreated)
        {
            Density.Dispose();
        }

        if (MaterialIds.IsCreated)
        {
            MaterialIds.Dispose();
        }
    }
}
