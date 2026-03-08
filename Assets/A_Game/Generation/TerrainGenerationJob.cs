using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

/// <summary>
/// Fills a chunk's full-resolution density and material grids from world-space terrain rules.
/// </summary>
[BurstCompile]
public struct TerrainGenerationJob : IJobParallelFor
{
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

        Density[index] = TerrainDensityUtility.SampleDensity(Settings, worldX, sampleY, worldZ);

        if (sampleX >= WorldConstants.ChunkSizeX ||
            sampleY >= WorldConstants.ChunkSizeY ||
            sampleZ >= WorldConstants.ChunkSizeZ)
        {
            return;
        }

        MaterialIds[WorldMath.CellIndex(sampleX, sampleY, sampleZ)] =
            TerrainDensityUtility.SampleMaterialId(Settings, worldX, sampleY, worldZ);
    }
}
