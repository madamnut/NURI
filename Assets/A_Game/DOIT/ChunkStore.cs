using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // Chunk(16 x 16 x 256, (cx, cz)) 단위의 런타임 오브젝트/상태를 보관하는 저장소.
    // Procedural 스트리밍에서:
    // - "현재 유지해야 하는 범위" 계산
    // - 범위 밖으로 나간 Chunk를 새 좌표로 Move(재사용)을 담당한다.
    public sealed class ChunkStore : MonoBehaviour
    {
        private sealed class Int2Comparer : IEqualityComparer<int2>
        {
            public bool Equals(int2 a, int2 b) => a.x == b.x && a.y == b.y;
            public int GetHashCode(int2 v)
            {
                unchecked
                {
                    return (v.x * 397) ^ v.y;
                }
            }
        }

        private readonly Dictionary<int2, ChunkProperties> _chunks = new Dictionary<int2, ChunkProperties>(new Int2Comparer());

        public int Count => _chunks.Count;

        public bool ContainsChunk(int2 chunkCoordinate)
        {
            return _chunks.ContainsKey(chunkCoordinate);
        }

        public bool TryGetChunk(int2 chunkCoordinate, out ChunkProperties chunk)
        {
            return _chunks.TryGetValue(chunkCoordinate, out chunk);
        }

        public void AddChunk(ChunkProperties chunk)
        {
            int2 coord = chunk.ChunkCoordinate;

            if (_chunks.ContainsKey(coord))
            {
                _chunks[coord] = chunk;
                return;
            }

            _chunks.Add(coord, chunk);
        }

        public bool RemoveChunk(int2 chunkCoordinate)
        {
            return _chunks.Remove(chunkCoordinate);
        }

        public IReadOnlyCollection<ChunkProperties> GetAllChunks()
        {
            return _chunks.Values;
        }

        // center를 기준으로 정사각 범위(|dx|<=range, |dz|<=range) "안쪽" 좌표 목록을 만든다.
        // 현재 스토어에 존재하는 Chunk만 대상으로 한다.
        public List<int2> GetChunkCoordinatesInsideRange(int2 center, int range)
        {
            var result = new List<int2>();

            foreach (KeyValuePair<int2, ChunkProperties> kv in _chunks)
            {
                int2 c = kv.Key;
                int dx = math.abs(c.x - center.x);
                int dz = math.abs(c.y - center.y);

                if (dx <= range && dz <= range)
                {
                    result.Add(c);
                }
            }

            return result;
        }

        // center를 기준으로 정사각 범위(|dx|<=range, |dz|<=range) "바깥" 좌표 목록을 만든다.
        // 현재 스토어에 존재하는 Chunk만 대상으로 한다.
        public List<int2> GetChunkCoordinatesOutsideOfRange(int2 center, int range)
        {
            var result = new List<int2>();

            foreach (KeyValuePair<int2, ChunkProperties> kv in _chunks)
            {
                int2 c = kv.Key;
                int dx = math.abs(c.x - center.x);
                int dz = math.abs(c.y - center.y);

                if (dx > range || dz > range)
                {
                    result.Add(c);
                }
            }

            return result;
        }

        // Chunk를 "재사용 이동"한다.
        // - Dictionary 키를 source -> target으로 변경
        // - Transform 위치를 target에 맞게 이동
        // - ChunkProperties의 좌표를 target으로 갱신
        // - 모든 SubChunk를 (다시 생성해야 하므로) Dirty로 표시하고 IsMeshGenerated를 false로 만든다.
        public void MoveChunk(int2 source, int2 target)
        {
            if (!_chunks.TryGetValue(source, out ChunkProperties chunk) || chunk == null)
            {
                return;
            }

            _chunks.Remove(source);
            _chunks[target] = chunk;

            Vector3 worldPos = new Vector3(
                target.x * WorldSettings.ChunkSize.x,
                0f,
                target.y * WorldSettings.ChunkSize.z
            );
            chunk.transform.position = worldPos;

            ChunkProperties.SubChunk[] subChunks = chunk.SubChunks;
            chunk.Initialize(target, subChunks);

            if (subChunks != null)
            {
                for (int i = 0; i < subChunks.Length; i++)
                {
                    ChunkProperties.SubChunk sc = subChunks[i];
                    if (sc == null)
                    {
                        continue;
                    }

                    sc.IsDirty = true;
                    sc.IsMeshGenerated = false;
                }
            }
        }
    }
}