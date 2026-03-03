using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // Procedural 지형 생성용 계산 잡 모음.
    //
    // 규칙(1차 고정):
    // - Chunk 데이터는 샘플 기준 17 x 257 x 17 (16 x 256 x 16 셀을 만들기 위한 N+1 샘플)
    // - density: 0(공기) ~ 255(고체)
    // - 높이맵 기반: (x,z)별 높이 h를 구해서 y <= h 이면 255 아니면 0
    //
    // 주의:
    // - Burst/Job에서 UnityEngine.Mathf.PerlinNoise는 직접 사용하지 않고
    //   Unity.Mathematics.noise.snoise(float2)를 사용한다.
    public static class ProceduralTerrainVoxelDataCalculationJob
    {
        [BurstCompile]
        public struct DensityJob : IJobParallelFor
        {
            public NativeArray<byte> Density;

            public int Sx; // 17
            public int Sy; // 257
            public int Sz; // 17

            public int BaseWorldX; // chunkWorldX
            public int BaseWorldZ; // chunkWorldZ

            public int BaseHeight;       // 0..255
            public int HeightVariation;  // >=0
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
                float2 p = new float2(worldX * NoiseScale, worldZ * NoiseScale);
                float n = noise.snoise(p);           // [-1..1]
                float normalized = (n + 1f) * 0.5f;  // [0..1]

                int h = BaseHeight + (int)math.round((normalized - 0.5f) * 2f * HeightVariation);
                return math.clamp(h, 0, WorldSettings.ChunkSize.y - 1);
            }
        }

        [BurstCompile]
        public struct ColorJob : IJobParallelFor
        {
            public NativeArray<Color32> Colors;

            public int Sx; // 17
            public int Sy; // 257
            public int Sz; // 17

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
                float n = noise.snoise(p);           // [-1..1]
                float normalized = (n + 1f) * 0.5f;  // [0..1]

                int h = BaseHeight + (int)math.round((normalized - 0.5f) * 2f * HeightVariation);
                return math.clamp(h, 0, WorldSettings.ChunkSize.y - 1);
            }
        }
    }
}