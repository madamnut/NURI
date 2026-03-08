using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 청크 하나의 density 샘플 배열을 높이맵 기반으로 채우는 Job이다.
///
/// 이 Job은 월드 샘플 좌표를 직접 사용해 높이를 계산한다.
/// 그래서 인접 청크가 경계를 공유할 때도 같은 경계 샘플 값을 얻게 되고,
/// 청크 seam 없이 이어지는 지형을 만들 수 있다.
///
/// 또한 현재 생성 방식은 이진값 생성이 아니라 연속 density field 생성이다.
/// 즉 표면 아래는 점점 255에 가까워지고, 표면 위는 점점 0에 가까워진다.
/// 표면 근처는 SurfaceFade 폭 안에서 부드럽게 128 근처를 지나가게 된다.
/// </summary>
[BurstCompile]
public struct TerrainGenerationJob : IJobParallelFor
{
    private const byte DirtMaterialId = 1;
    private const byte RockMaterialId = 2;
    private const float DirtLayerDepth = 4f;

    public ChunkCoord Coord;
    public TerrainGenerationSettings Settings;

    public NativeArray<byte> Density;

    [NativeDisableParallelForRestriction]
    [WriteOnly]
    public NativeArray<byte> MaterialIds;

    public void Execute(int index)
    {
        int sampleX = index % WorldConstants.SampleSizeX;
        int sampleZ = (index / WorldConstants.SampleSizeX) % WorldConstants.SampleSizeZ;
        int sampleY = index / (WorldConstants.SampleSizeX * WorldConstants.SampleSizeZ);

        int worldX = Coord.X * WorldConstants.ChunkSizeX + sampleX;
        int worldZ = Coord.Z * WorldConstants.ChunkSizeZ + sampleZ;

        float2 noiseCoord = new float2(worldX, worldZ) * Settings.NoiseScale;
        float noiseValue = noise.cnoise(noiseCoord);
        float normalizedNoise = noiseValue * 0.5f + 0.5f;
        float height = Settings.BaseHeight + normalizedNoise * Settings.HeightAmplitude;

        float fade = math.max(0.001f, Settings.SurfaceFade);

        // height와 샘플 Y의 차이를 이용해 연속적인 충만도 필드를 만든다.
        // sampleY == height 근처에서는 density가 threshold(128) 부근을 지나간다.
        float signedDistance = height - sampleY;
        float normalizedDensity = signedDistance / fade * 0.5f + 0.5f;
        float clampedDensity = math.clamp(normalizedDensity, 0f, 1f);

        Density[index] = (byte)math.round(clampedDensity * WorldConstants.FullDensity);

        if (sampleX >= WorldConstants.ChunkSizeX ||
            sampleY >= WorldConstants.ChunkSizeY ||
            sampleZ >= WorldConstants.ChunkSizeZ)
        {
            return;
        }

        float cellCenterY = sampleY + 0.5f;
        float surfaceDepth = height - cellCenterY;
        byte materialId = surfaceDepth <= DirtLayerDepth ? DirtMaterialId : RockMaterialId;
        MaterialIds[WorldMath.CellIndex(sampleX, sampleY, sampleZ)] = materialId;
    }
}
