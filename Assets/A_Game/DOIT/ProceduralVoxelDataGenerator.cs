using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // Procedural 방식으로 Chunk(16 x 16 x 256, (cx, cz))의 샘플 데이터를 생성한다.
    //
    // 규칙:
    // - 밀도/색 데이터는 VoxelDataStore / VoxelColorStore가 Chunk 단위(17 x 257 x 17 샘플)로 보관한다.
    // - 이 클래스는 "잡 시스템"으로 해당 NativeArray를 채우는 역할만 한다.
    //
    // 사용 패턴:
    // - GenerateForChunkImmediate(coord): 즉시 생성(잡 스케줄 후 Complete)
    // - ScheduleGenerateForChunk(coord): 스케줄만 하고, 나중에 CompleteIfScheduled(coord)로 완료
    public sealed class ProceduralVoxelDataGenerator : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private VoxelDataStore _voxelDataStore;
        [SerializeField] private VoxelColorStore _voxelColorStore;

        [Header("지형 파라미터(1차 고정 규칙)")]
        [SerializeField] private int _baseHeight = 64;        // 0..255
        [SerializeField] private int _heightVariation = 24;   // 0..255
        [SerializeField] private float _noiseScale = 0.02f;

        [Header("색 파라미터(1차 고정 규칙)")]
        [SerializeField] private Color32 _solidColor = new Color32(110, 85, 60, 255);
        [SerializeField] private Color32 _emptyColor = new Color32(0, 0, 0, 0);

        private sealed class Int2Comparer : IEqualityComparer<int2>
        {
            public bool Equals(int2 a, int2 b) => a.x == b.x && a.y == b.y;
            public int GetHashCode(int2 v)
            {
                unchecked { return (v.x * 397) ^ v.y; }
            }
        }

        private readonly Dictionary<int2, JobHandle> _densityJobs = new Dictionary<int2, JobHandle>(new Int2Comparer());
        private readonly Dictionary<int2, JobHandle> _colorJobs = new Dictionary<int2, JobHandle>(new Int2Comparer());

        public void GenerateForChunkImmediate(int2 chunkCoordinate)
        {
            ScheduleGenerateForChunk(chunkCoordinate);
            CompleteIfScheduled(chunkCoordinate);
        }

        public void ScheduleGenerateForChunk(int2 chunkCoordinate)
        {
            if (_voxelDataStore == null)
            {
                Debug.LogError("ProceduralVoxelDataGenerator: VoxelDataStore가 할당되지 않았습니다.");
                return;
            }

            _voxelDataStore.EnsureChunkDataExists(chunkCoordinate);

            if (_voxelDataStore.TryGetChunkDensityBuffer(chunkCoordinate, out NativeArray<byte> density))
            {
                int sx = WorldSettings.ChunkSize.x + 1; // 17
                int sy = WorldSettings.ChunkSize.y + 1; // 257
                int sz = WorldSettings.ChunkSize.z + 1; // 17

                int baseWorldX = chunkCoordinate.x * WorldSettings.ChunkSize.x;
                int baseWorldZ = chunkCoordinate.y * WorldSettings.ChunkSize.z;

                var job = new DensityJob
                {
                    Density = density,
                    Sx = sx,
                    Sy = sy,
                    Sz = sz,
                    BaseWorldX = baseWorldX,
                    BaseWorldZ = baseWorldZ,
                    BaseHeight = math.clamp(_baseHeight, 0, WorldSettings.ChunkSize.y - 1),
                    HeightVariation = math.max(0, _heightVariation),
                    NoiseScale = _noiseScale
                };

                JobHandle handle = job.Schedule(density.Length, 256);
                _densityJobs[chunkCoordinate] = handle;
            }

            if (_voxelColorStore != null)
            {
                _voxelColorStore.EnsureChunkColorDataExists(chunkCoordinate);

                if (_voxelColorStore.TryGetChunkColorBuffer(chunkCoordinate, out NativeArray<Color32> colors))
                {
                    int sx = WorldSettings.ChunkSize.x + 1; // 17
                    int sy = WorldSettings.ChunkSize.y + 1; // 257
                    int sz = WorldSettings.ChunkSize.z + 1; // 17

                    int baseWorldX = chunkCoordinate.x * WorldSettings.ChunkSize.x;
                    int baseWorldZ = chunkCoordinate.y * WorldSettings.ChunkSize.z;

                    var job = new ColorJob
                    {
                        Colors = colors,
                        Sx = sx,
                        Sy = sy,
                        Sz = sz,
                        BaseWorldX = baseWorldX,
                        BaseWorldZ = baseWorldZ,
                        BaseHeight = math.clamp(_baseHeight, 0, WorldSettings.ChunkSize.y - 1),
                        HeightVariation = math.max(0, _heightVariation),
                        NoiseScale = _noiseScale,
                        Solid = _solidColor,
                        Empty = _emptyColor
                    };

                    JobHandle handle = job.Schedule(colors.Length, 256);
                    _colorJobs[chunkCoordinate] = handle;
                }
            }
        }

        public void CompleteIfScheduled(int2 chunkCoordinate)
        {
            if (_densityJobs.TryGetValue(chunkCoordinate, out JobHandle dh))
            {
                dh.Complete();
                _densityJobs.Remove(chunkCoordinate);
            }

            if (_colorJobs.TryGetValue(chunkCoordinate, out JobHandle ch))
            {
                ch.Complete();
                _colorJobs.Remove(chunkCoordinate);
            }
        }

        public void CompleteAll()
        {
            foreach (var kv in _densityJobs)
            {
                kv.Value.Complete();
            }
            _densityJobs.Clear();

            foreach (var kv in _colorJobs)
            {
                kv.Value.Complete();
            }
            _colorJobs.Clear();
        }

        private void OnDestroy()
        {
            // 잡이 남아있으면 안전하게 완료하고 종료
            CompleteAll();
        }

        [BurstCompile]
        private struct DensityJob : IJobParallelFor
        {
            public NativeArray<byte> Density;

            public int Sx;
            public int Sy;
            public int Sz;

            public int BaseWorldX;
            public int BaseWorldZ;

            public int BaseHeight;
            public int HeightVariation;
            public float NoiseScale;

            public void Execute(int index)
            {
                // index -> (x,y,z)
                int x = index % Sx;
                int t = index / Sx;
                int z = t % Sz;
                int y = t / Sz;

                int wx = BaseWorldX + x;
                int wz = BaseWorldZ + z;

                int h = GetHeight(wx, wz);
                Density[index] = (y <= h) ? (byte)255 : (byte)0;
            }

            private int GetHeight(int worldX, int worldZ)
            {
                // Burst 호환을 위해 Unity.Mathematics.noise 사용
                float2 p = new float2(worldX * NoiseScale, worldZ * NoiseScale);
                float n = noise.snoise(p); // [-1..1]
                float normalized = (n + 1f) * 0.5f; // [0..1]

                int h = BaseHeight + (int)math.round((normalized - 0.5f) * 2f * HeightVariation);
                return math.clamp(h, 0, WorldSettings.ChunkSize.y - 1);
            }
        }

        [BurstCompile]
        private struct ColorJob : IJobParallelFor
        {
            public NativeArray<Color32> Colors;

            public int Sx;
            public int Sy;
            public int Sz;

            public int BaseWorldX;
            public int BaseWorldZ;

            public int BaseHeight;
            public int HeightVariation;
            public float NoiseScale;

            public Color32 Solid;
            public Color32 Empty;

            public void Execute(int index)
            {
                int x = index % Sx;
                int t = index / Sx;
                int z = t % Sz;
                int y = t / Sz;

                int wx = BaseWorldX + x;
                int wz = BaseWorldZ + z;

                int h = GetHeight(wx, wz);
                Colors[index] = (y <= h) ? Solid : Empty;
            }

            private int GetHeight(int worldX, int worldZ)
            {
                float2 p = new float2(worldX * NoiseScale, worldZ * NoiseScale);
                float n = noise.snoise(p); // [-1..1]
                float normalized = (n + 1f) * 0.5f; // [0..1]

                int h = BaseHeight + (int)math.round((normalized - 0.5f) * 2f * HeightVariation);
                return math.clamp(h, 0, WorldSettings.ChunkSize.y - 1);
            }
        }
    }
}