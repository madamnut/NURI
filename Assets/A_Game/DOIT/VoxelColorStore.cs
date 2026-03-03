using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

namespace NURI
{
    // Chunk(16 x 16 x 256, (cx, cz)) 단위의 색 샘플 데이터를 보관한다.
    //
    // 마칭큐브 전제:
    // - 셀(16 x 256 x 16)을 메싱하려면 샘플 포인트는 (N+1) 필요
    // - 따라서 Chunk 색 데이터도 17 x 257 x 17 크기의 "샘플"을 가진다.
    //
    // 규칙:
    // - 데이터는 Chunk 단위로만 저장/이동한다.
    // - 메시/콜라이더 갱신은 SubChunk 단위이므로, 색 변경 시 영향을 받는 SubChunk를 Dirty로 마킹한다.
    public sealed class VoxelColorStore : MonoBehaviour
    {
        private sealed class Int2Comparer : IEqualityComparer<int2>
        {
            public bool Equals(int2 a, int2 b) => a.x == b.x && a.y == b.y;
            public int GetHashCode(int2 v)
            {
                unchecked { return (v.x * 397) ^ v.y; }
            }
        }

        private sealed class ChunkColorData
        {
            public NativeArray<Color32> Color; // 17 x 257 x 17
            public bool IsCreated;
        }

        [Header("참조")]
        [SerializeField] private ChunkStore _chunkStore;

        private readonly Dictionary<int2, ChunkColorData> _data = new Dictionary<int2, ChunkColorData>(new Int2Comparer());

        private int _sx;
        private int _sy;
        private int _sz;

        private void Awake()
        {
            _sx = WorldSettings.ChunkSize.x + 1; // 17
            _sy = WorldSettings.ChunkSize.y + 1; // 257
            _sz = WorldSettings.ChunkSize.z + 1; // 17
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<int2, ChunkColorData> kv in _data)
            {
                ChunkColorData cd = kv.Value;
                if (cd != null && cd.IsCreated && cd.Color.IsCreated)
                {
                    cd.Color.Dispose();
                }
            }

            _data.Clear();
        }

        public bool HasChunkColorData(int2 chunkCoordinate)
        {
            return _data.ContainsKey(chunkCoordinate);
        }

        public void EnsureChunkColorDataExists(int2 chunkCoordinate)
        {
            if (_data.TryGetValue(chunkCoordinate, out ChunkColorData cd) && cd != null && cd.IsCreated)
            {
                return;
            }

            CreateChunkColorData(chunkCoordinate);
        }

        public void CreateChunkColorData(int2 chunkCoordinate)
        {
            if (_data.TryGetValue(chunkCoordinate, out ChunkColorData existing) && existing != null && existing.IsCreated)
            {
                return;
            }

            var cd = new ChunkColorData();
            cd.Color = new NativeArray<Color32>(_sx * _sy * _sz, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            cd.IsCreated = true;

            _data[chunkCoordinate] = cd;
        }

        // Chunk 색 데이터를 "재사용 이동"한다. (Procedural 스트리밍용)
        // source의 NativeArray를 target 키로 옮긴다.
        public void MoveChunkColorData(int2 source, int2 target)
        {
            if (!_data.TryGetValue(source, out ChunkColorData cd) || cd == null || !cd.IsCreated)
            {
                return;
            }

            // target에 기존 데이터가 있으면 정리
            if (_data.TryGetValue(target, out ChunkColorData oldTarget) && oldTarget != null && oldTarget.IsCreated)
            {
                if (oldTarget.Color.IsCreated)
                {
                    oldTarget.Color.Dispose();
                }
            }

            _data.Remove(source);
            _data[target] = cd;
        }

        // 월드 샘플 좌표(정수, 1유닛 복셀 그리드) -> (chunk, localSampleX/Y/Z)
        private void WorldSampleToChunkLocal(in int3 worldSamplePos, out int2 chunkCoord, out int lsx, out int lsy, out int lsz)
        {
            chunkCoord = CoordinateUtilities.VoxelToChunkCoordinate(worldSamplePos);

            int chunkWorldX = chunkCoord.x * WorldSettings.ChunkSize.x;
            int chunkWorldZ = chunkCoord.y * WorldSettings.ChunkSize.z;

            lsx = worldSamplePos.x - chunkWorldX; // 0..16
            lsz = worldSamplePos.z - chunkWorldZ; // 0..16

            // y는 Chunk가 0..256을 모두 포함한다고 가정 (단일 수직 Chunk)
            lsy = worldSamplePos.y; // 0..256
        }

        private int GetIndex(int x, int y, int z)
        {
            // x: [0.._sx-1], y: [0.._sy-1], z: [0.._sz-1]
            return (y * _sz + z) * _sx + x;
        }

        public Color32 GetColorSample(in int3 worldSamplePos)
        {
            WorldSampleToChunkLocal(worldSamplePos, out int2 chunkCoord, out int x, out int y, out int z);

            if (!_data.TryGetValue(chunkCoord, out ChunkColorData cd) || cd == null || !cd.IsCreated)
            {
                return default;
            }

            if ((uint)x >= (uint)_sx || (uint)y >= (uint)_sy || (uint)z >= (uint)_sz)
            {
                return default;
            }

            return cd.Color[GetIndex(x, y, z)];
        }

        public void SetColorSample(in int3 worldSamplePos, Color32 value)
        {
            WorldSampleToChunkLocal(worldSamplePos, out int2 chunkCoord, out int x, out int y, out int z);

            EnsureChunkColorDataExists(chunkCoord);
            ChunkColorData cd = _data[chunkCoord];

            if ((uint)x >= (uint)_sx || (uint)y >= (uint)_sy || (uint)z >= (uint)_sz)
            {
                return;
            }

            int idx = GetIndex(x, y, z);
            if (cd.Color[idx].Equals(value))
            {
                return;
            }

            cd.Color[idx] = value;

            // 색 수정 -> 영향받는 SubChunk Dirty 마킹
            MarkDirtyFromWorldSample(worldSamplePos);
        }

        // bounds 내의 "월드 샘플 좌표"를 순회하며 수정한다.
        // - bounds는 BoundsInt 규칙대로 max는 제외된다.
        // - editFunc: (worldSamplePos, oldColor) -> newColor
        public void SetColorCustom(BoundsInt bounds, Func<int3, Color32, Color32> editFunc)
        {
            if (editFunc == null)
            {
                return;
            }

            int3 min = new int3(bounds.min.x, bounds.min.y, bounds.min.z);
            int3 max = new int3(bounds.max.x, bounds.max.y, bounds.max.z);

            for (int y = min.y; y < max.y; y++)
            {
                for (int z = min.z; z < max.z; z++)
                {
                    for (int x = min.x; x < max.x; x++)
                    {
                        var p = new int3(x, y, z);

                        WorldSampleToChunkLocal(p, out int2 chunkCoord, out int lx, out int ly, out int lz);

                        // y 범위 밖은 무시 (0..256)
                        if ((uint)ly >= (uint)_sy)
                        {
                            continue;
                        }

                        EnsureChunkColorDataExists(chunkCoord);
                        ChunkColorData cd = _data[chunkCoord];

                        // x/z 샘플 범위 밖은 무시 (0..16)
                        if ((uint)lx >= (uint)_sx || (uint)lz >= (uint)_sz)
                        {
                            continue;
                        }

                        int idx = GetIndex(lx, ly, lz);
                        Color32 oldVal = cd.Color[idx];
                        Color32 newVal = editFunc(p, oldVal);

                        if (oldVal.Equals(newVal))
                        {
                            continue;
                        }

                        cd.Color[idx] = newVal;
                        MarkDirtyFromWorldSample(p);
                    }
                }
            }
        }

        private void MarkDirtyFromWorldSample(in int3 worldSamplePos)
        {
            if (_chunkStore == null)
            {
                return;
            }

            int2 chunkCoord = CoordinateUtilities.VoxelToChunkCoordinate(worldSamplePos);

            if (!_chunkStore.TryGetChunk(chunkCoord, out ChunkProperties chunk) || chunk == null)
            {
                return;
            }

            int voxelY = worldSamplePos.y;
            int sy = CoordinateUtilities.VoxelYToSubChunkIndex(voxelY);

            chunk.MarkDirty(sy);

            int ly = voxelY - (sy * WorldSettings.SubChunkSize.y); // 0..15 또는 16(샘플 경계)
            if (ly <= 0 && sy - 1 >= 0)
            {
                chunk.MarkDirty(sy - 1);
            }
            if (ly >= WorldSettings.SubChunkSize.y - 1 && sy + 1 < chunk.SubChunks.Length)
            {
                chunk.MarkDirty(sy + 1);
            }

            int2 localXZ = CoordinateUtilities.VoxelToChunkLocalXZ(worldSamplePos);
            if (localXZ.x <= 0)
            {
                MarkNeighborDirty(new int2(chunkCoord.x - 1, chunkCoord.y), sy);
            }
            if (localXZ.x >= WorldSettings.ChunkSize.x - 1)
            {
                MarkNeighborDirty(new int2(chunkCoord.x + 1, chunkCoord.y), sy);
            }
            if (localXZ.y <= 0)
            {
                MarkNeighborDirty(new int2(chunkCoord.x, chunkCoord.y - 1), sy);
            }
            if (localXZ.y >= WorldSettings.ChunkSize.z - 1)
            {
                MarkNeighborDirty(new int2(chunkCoord.x, chunkCoord.y + 1), sy);
            }
        }

        private void MarkNeighborDirty(int2 neighborChunkCoord, int sy)
        {
            if (_chunkStore == null)
            {
                return;
            }

            if (!_chunkStore.TryGetChunk(neighborChunkCoord, out ChunkProperties neighbor) || neighbor == null)
            {
                return;
            }

            if (sy < 0 || neighbor.SubChunks == null || sy >= neighbor.SubChunks.Length)
            {
                return;
            }

            neighbor.MarkDirty(sy);
        }

        // 메싱 단계에서 Chunk의 색 샘플 버퍼를 직접 읽고 싶을 때 사용한다.
        // 반환된 NativeArray는 store가 소유하므로 Dispose하면 안 된다.
        public bool TryGetChunkColorBuffer(int2 chunkCoordinate, out NativeArray<Color32> colorBuffer)
        {
            if (_data.TryGetValue(chunkCoordinate, out ChunkColorData cd) && cd != null && cd.IsCreated)
            {
                colorBuffer = cd.Color;
                return true;
            }

            colorBuffer = default;
            return false;
        }

        public int3 GetChunkSampleDimensions()
        {
            return new int3(_sx, _sy, _sz); // (17, 257, 17)
        }
    }
}