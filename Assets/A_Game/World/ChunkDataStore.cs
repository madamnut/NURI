using System;
using System.Collections.Generic;

/// <summary>
/// 월드에 현재 로드되어 있는 청크 데이터를 보관하는 저장소이다.
///
/// 청크 자체는 NativeArray를 들고 있으므로, 단순히 딕셔너리에서 제거하는 것만으로는
/// 메모리가 해제되지 않는다. 따라서 청크를 제거할 때는 반드시 Dispose를 함께 호출해야 한다.
///
/// 이 클래스를 별도로 두는 이유는, 월드 시스템이 "월드 흐름 제어"에 집중하고
/// 저장/조회/정리 책임은 이 저장소에 위임하기 위해서이다.
/// </summary>
public sealed class ChunkDataStore : IDisposable
{
    private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new Dictionary<ChunkCoord, ChunkData>();

    public int Count => _chunks.Count;

    public void Add(ChunkData chunk)
    {
        _chunks.Add(chunk.Coord, chunk);
    }

    public bool TryGet(ChunkCoord coord, out ChunkData chunk)
    {
        return _chunks.TryGetValue(coord, out chunk);
    }

    public bool Contains(ChunkCoord coord)
    {
        return _chunks.ContainsKey(coord);
    }

    public bool RemoveAndDispose(ChunkCoord coord)
    {
        if (!_chunks.TryGetValue(coord, out ChunkData chunk))
        {
            return false;
        }

        _chunks.Remove(coord);
        chunk.Dispose();
        return true;
    }

    public IEnumerable<KeyValuePair<ChunkCoord, ChunkData>> Enumerate()
    {
        return _chunks;
    }

    public void ClearAndDispose()
    {
        foreach (ChunkData chunk in _chunks.Values)
        {
            chunk.Dispose();
        }

        _chunks.Clear();
    }

    public void Dispose()
    {
        ClearAndDispose();
    }
}
