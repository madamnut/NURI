// Assets/Scripts/World/HeightmapDensityGenerator.cs
// 새 파일

using UnityEngine;

public sealed class HeightmapDensityGenerator
{
    // 높이맵 기반으로 코너 샘플 채우기
    // - worldX/worldZ에 대해 Perlin으로 height를 만들고
    // - worldY < height 이면 고체(128), 아니면 공기(0)
    //
    // 1유닛 = 1복셀(정점 간 거리 = 1) 전제

    private readonly float _noiseScale;
    private readonly float _baseHeight;
    private readonly float _amplitude;

    private readonly byte _solidAmount;
    private readonly byte _airAmount;

    public HeightmapDensityGenerator(float noiseScale, float baseHeight, float amplitude, byte solidAmount = 128, byte airAmount = 0)
    {
        _noiseScale = noiseScale;
        _baseHeight = baseHeight;
        _amplitude = amplitude;
        _solidAmount = solidAmount;
        _airAmount = airAmount;
    }

    // (cx, cz) 컬럼의 코너 샘플(17x257x17)을 월드 좌표 기반으로 채움
    public void FillColumn(int chunkX, int chunkZ, ChunkColumnData data)
    {
        int worldOriginX = chunkX * Chunk.SizeX;
        int worldOriginZ = chunkZ * Chunk.SizeZ;

        for (int y = 0; y < ChunkColumnData.SampleSizeY; y++)
        {
            for (int z = 0; z < ChunkColumnData.SampleSizeZ; z++)
            {
                int worldZ = worldOriginZ + z;

                for (int x = 0; x < ChunkColumnData.SampleSizeX; x++)
                {
                    int worldX = worldOriginX + x;

                    float height = SampleHeight(worldX, worldZ);

                    // 코너의 월드 Y 자체가 "정점 높이"임(1유닛=1복셀)
                    byte amount = (y < height) ? _solidAmount : _airAmount;
                    data.Set(x, y, z, amount);
                }
            }
        }
    }

    private float SampleHeight(int worldX, int worldZ)
    {
        float nx = worldX * _noiseScale;
        float nz = worldZ * _noiseScale;

        // Mathf.PerlinNoise는 0..1
        float n = Mathf.PerlinNoise(nx, nz);

        // baseHeight +/- amplitude 범위로 변환
        return _baseHeight + (n - 0.5f) * 2f * _amplitude;
    }
}