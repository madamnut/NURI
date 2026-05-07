using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

/// <summary>
/// Fills a chunk's cell-based material id/amount grids from world-space terrain rules.
/// </summary>
[BurstCompile]
public struct TerrainGenerationJob : IJobParallelFor
{
    public ChunkCoord Coord;
    public TerrainGenerationSettings Settings;

    [NativeDisableParallelForRestriction]
    [WriteOnly]
    public NativeArray<byte> MaterialIds;

    [NativeDisableParallelForRestriction]
    [WriteOnly]
    public NativeArray<byte> MaterialAmounts;

    public void Execute(int index)
    {
        int cellX = index % WorldConstants.ChunkSizeX;
        int cellZ = (index / WorldConstants.ChunkSizeX) % WorldConstants.ChunkSizeZ;
        int cellY = index / (WorldConstants.ChunkSizeX * WorldConstants.ChunkSizeZ);

        int worldX = Coord.X * WorldConstants.ChunkSizeX + cellX;
        int worldZ = Coord.Z * WorldConstants.ChunkSizeZ + cellZ;

        byte amount = TerrainDensityUtility.SampleCellAmount(Settings, worldX, cellY, worldZ);
        MaterialAmounts[index] = amount;
        MaterialIds[index] = TerrainDensityUtility.SampleMaterialId(Settings, worldX, cellY, worldZ, amount);
    }
}
