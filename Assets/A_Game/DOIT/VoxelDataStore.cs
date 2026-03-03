using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

namespace NURI
{
    // Chunk(16 x 16 x 256, (cx, cz)) 단위의 밀도(스칼라필드) 샘플 데이터를 보관한다.
    //
    // 마칭큐브 전제:
    // - 셀(16 x 256 x 16)을 메싱하려면 샘플 포인트는 (N+1) 필요
    // - 따라서 Chunk 데이터는 17 x 257 x 17 크기의 "샘플"을 가진다.
    //
    // - 데이터는 Chunk 단위로만 저장/이동한다.
    // - 메시/콜라이더 갱신은 SubChunk 단위이므로, 데이터 변경 시 영향을 받는 SubChunk를 Dirty로 마킹한다.
    public sealed class VoxelDataStore : MonoBehaviour
    {
        private sealed class Int2Comparer : IEqualityComparer<int2>
        {
            public bool Equals(int2 a, int2 b) => a.x == b.x && a.y == b.y;
            public int GetHashCode(int2 v)
            {
                unchecked { return (v.x * 397) ^ v.y; }
            }
        }

        private sealed class ChunkData
        {
            public NativeArray<byte> Density; // 17 x 257 x 17
            public bool IsCreated;
        }

        [Header("참조")]
        [SerializeField] private ChunkStore _chunkStore;

        private readonly Dictionary<int2, ChunkData> _data = new Dictionary<int2, ChunkData>(new Int2Comparer());

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
            foreach (KeyValuePair<int2, ChunkData> kv in _data)
            {
                ChunkData cd = kv.Value;
                if (cd != null && cd.IsCreated && cd.Density.IsCreated)
                {
                    cd.Density.Dispose();
                }
            }

            _data.Clear();
        }

        public bool HasChunkData(int2 chunkCoordinate)
        {
            return _data.ContainsKey(chunkCoordinate);
        }

        public void EnsureChunkDataExists(int2 chunkCoordinate)
        {
            if (_data.TryGetValue(chunkCoordinate, out ChunkData cd) && cd != null && cd.IsCreated)
            {
                return;
            }

            CreateChunkData(chunkCoordinate);
        }

        public void CreateChunkData(int2 chunkCoordinate)
        {
            if (_data.TryGetValue(chunkCoordinate, out ChunkData existing) && existing != null && existing.IsCreated)
            {
                return;
            }

            var cd = new ChunkData();
            cd.Density = new NativeArray<byte>(_sx * _sy * _sz, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            cd.IsCreated = true;

            _data[chunkCoordinate] = cd;
        }

        // Chunk 데이터를 "재사용 이동"한다. (Procedural 스트리밍용)
        // source의 NativeArray를 target 키로 옮긴다.
        public void MoveChunkData(int2 source, int2 target)
        {
            if (!_data.TryGetValue(source, out ChunkData cd) || cd == null || !cd.IsCreated)
            {
                return;
            }

            // target에 기존 데이터가 있으면 정리
            if (_data.TryGetValue(target, out ChunkData oldTarget) && oldTarget != null && oldTarget.IsCreated)
            {
                if (oldTarget.Density.IsCreated)
                {
                    oldTarget.Density.Dispose();
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

        public byte GetDensitySample(in int3 worldSamplePos)
        {
            WorldSampleToChunkLocal(worldSamplePos, out int2 chunkCoord, out int x, out int y, out int z);

            if (!_data.TryGetValue(chunkCoord, out ChunkData cd) || cd == null || !cd.IsCreated)
            {
                return 0;
            }

            if ((uint)x >= (uint)_sx || (uint)y >= (uint)_sy || (uint)z >= (uint)_sz)
            {
                return 0;
            }

            return cd.Density[GetIndex(x, y, z)];
        }

        public void SetDensitySample(in int3 worldSamplePos, byte value)
        {
            WorldSampleToChunkLocal(worldSamplePos, out int2 chunkCoord, out int x, out int y, out int z);

            EnsureChunkDataExists(chunkCoord);
            ChunkData cd = _data[chunkCoord];

            if ((uint)x >= (uint)_sx || (uint)y >= (uint)_sy || (uint)z >= (uint)_sz)
            {
                return;
            }

            int idx = GetIndex(x, y, z);
            if (cd.Density[idx] == value)
            {
                return;
            }

            cd.Density[idx] = value;

            // 데이터 수정 -> 영향받는 SubChunk Dirty 마킹
            MarkDirtyFromWorldSample(worldSamplePos);
        }

        // bounds 내의 "월드 샘플 좌표"를 순회하며 수정한다.
        // - bounds는 BoundsInt 규칙대로 max는 제외된다.
        // - editFunc: (worldSamplePos, oldDensity) -> newDensity
        public void SetDensityCustom(BoundsInt bounds, Func<int3, byte, byte> editFunc)
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

                        EnsureChunkDataExists(chunkCoord);
                        ChunkData cd = _data[chunkCoord];

                        // x/z 샘플 범위 밖은 무시 (0..16)
                        if ((uint)lx >= (uint)_sx || (uint)lz >= (uint)_sz)
                        {
                            continue;
                        }

                        int idx = GetIndex(lx, ly, lz);
                        byte oldVal = cd.Density[idx];
                        byte newVal = editFunc(p, oldVal);

                        if (newVal == oldVal)
                        {
                            continue;
                        }

                        cd.Density[idx] = newVal;
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

            // 샘플 y는 0..256. 메시(서브청크) 영향은 셀 기준으로 보는 게 자연스럽다.
            // 여기서는 간단히 "y가 속한 sy"를 Dirty로 보고, 경계(0/15)에 닿으면 이웃도 Dirty로 본다.
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

            // x/z 경계에서 이웃 Chunk의 같은 sy도 Dirty 처리
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

        // 메싱 단계에서 Chunk의 샘플 버퍼를 직접 읽고 싶을 때 사용한다.
        // 반환된 NativeArray는 store가 소유하므로 Dispose하면 안 된다.
        public bool TryGetChunkDensityBuffer(int2 chunkCoordinate, out NativeArray<byte> densityBuffer)
        {
            if (_data.TryGetValue(chunkCoordinate, out ChunkData cd) && cd != null && cd.IsCreated)
            {
                densityBuffer = cd.Density;
                return true;
            }

            densityBuffer = default;
            return false;
        }

        public int3 GetChunkSampleDimensions()
        {
            return new int3(_sx, _sy, _sz); // (17, 257, 17)
        }
    }
}