using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct TerrainSampleDensityFromCellsJob : IJobParallelFor
{
    public ChunkCoord Coord;
    public TerrainGenerationSettings Settings;

    [ReadOnly] public NativeArray<byte> MaterialAmounts;
    [WriteOnly] public NativeArray<sbyte> Density;

    public void Execute(int index)
    {
        int sampleX = index % WorldConstants.SampleSizeX;
        int sampleZ = (index / WorldConstants.SampleSizeX) % WorldConstants.SampleSizeZ;
        int sampleY = index / (WorldConstants.SampleSizeX * WorldConstants.SampleSizeZ);

        if (sampleX > 0 && sampleX < WorldConstants.SampleSizeX - 1 &&
            sampleY > 0 && sampleY < WorldConstants.SampleSizeY - 1 &&
            sampleZ > 0 && sampleZ < WorldConstants.SampleSizeZ - 1)
        {
            int totalDensity = 0;

            for (int offsetZ = 0; offsetZ <= 1; offsetZ++)
            {
                for (int offsetY = 0; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = 0; offsetX <= 1; offsetX++)
                    {
                        int cellX = sampleX - 1 + offsetX;
                        int cellY = sampleY - 1 + offsetY;
                        int cellZ = sampleZ - 1 + offsetZ;
                        byte amount = MaterialAmounts[WorldMath.CellIndex(cellX, cellY, cellZ)];
                        totalDensity += TerrainDensityUtility.CellAmountToMeshingDensity(amount);
                    }
                }
            }

            int averageDensity = (totalDensity + 4) / 8;
            Density[index] = (sbyte)math.clamp(averageDensity, WorldConstants.EmptyDensity, WorldConstants.FullDensity);
            return;
        }

        int worldX = Coord.X * WorldConstants.ChunkSizeX + sampleX;
        int worldZ = Coord.Z * WorldConstants.ChunkSizeZ + sampleZ;
        Density[index] = TerrainDensityUtility.SampleDensity(Settings, worldX, sampleY, worldZ);
    }
}
