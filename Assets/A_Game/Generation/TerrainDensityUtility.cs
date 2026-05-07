using Unity.Mathematics;

/// <summary>
/// World-space terrain density/material sampling shared by generation and LOD meshing.
/// </summary>
public static class TerrainDensityUtility
{
    public const byte AirMaterialId = 0;
    public const byte DirtMaterialId = 1;
    public const byte RockMaterialId = 2;

    public static sbyte SampleDensity(TerrainGenerationSettings settings, int worldX, int sampleY, int worldZ)
    {
        int totalDensity = 0;

        for (int offsetZ = 0; offsetZ <= 1; offsetZ++)
        {
            for (int offsetY = 0; offsetY <= 1; offsetY++)
            {
                for (int offsetX = 0; offsetX <= 1; offsetX++)
                {
                    byte amount = SampleCellAmount(
                        settings,
                        worldX - 1 + offsetX,
                        sampleY - 1 + offsetY,
                        worldZ - 1 + offsetZ);
                    totalDensity += CellAmountToMeshingDensity(amount);
                }
            }
        }

        int averageDensity = (int)math.round(totalDensity / 8f);
        return (sbyte)math.clamp(averageDensity, WorldConstants.EmptyDensity, WorldConstants.FullDensity);
    }

    public static byte SampleMaterialId(TerrainGenerationSettings settings, int worldX, int cellY, int worldZ)
    {
        byte amount = SampleCellAmount(settings, worldX, cellY, worldZ);
        return SampleMaterialId(settings, worldX, cellY, worldZ, amount);
    }

    public static byte SampleMaterialId(TerrainGenerationSettings settings, int worldX, int cellY, int worldZ, byte amount)
    {
        if (amount == 0)
        {
            return AirMaterialId;
        }

        float cellCenterY = cellY + 0.5f;
        float rockHeight = SampleRockHeight(settings, worldX, worldZ);
        float dirtHeight = SampleDirtHeight(settings, worldX, worldZ);
        bool dirtCoversRock = dirtHeight >= rockHeight;

        if (cellCenterY <= rockHeight)
        {
            return RockMaterialId;
        }

        if (dirtCoversRock && cellCenterY <= dirtHeight)
        {
            return DirtMaterialId;
        }

        return dirtCoversRock ? DirtMaterialId : RockMaterialId;
    }

    public static byte SampleCellAmount(TerrainGenerationSettings settings, int worldX, int cellY, int worldZ)
    {
        if (cellY < 0 || cellY >= WorldConstants.ChunkSizeY)
        {
            return 0;
        }

        float centerX = worldX + 0.5f;
        float centerY = cellY + 0.5f;
        float centerZ = worldZ + 0.5f;
        float surfaceHeight = SampleSurfaceHeight(settings, centerX, centerZ);
        float fade = math.max(0.001f, settings.SurfaceFade);
        float signedDistance = surfaceHeight - centerY;
        float normalizedFill = signedDistance / (fade * 2f) + 0.5f;
        float clampedFill = math.clamp(normalizedFill, 0f, 1f);
        return (byte)math.round(clampedFill * 255f);
    }

    public static sbyte AmountToDensity(int amount)
    {
        int clampedAmount = math.clamp(amount, 0, 255);
        return (sbyte)(clampedAmount - 128);
    }

    public static sbyte CellAmountToMeshingDensity(int amount)
    {
        int clampedAmount = math.clamp(amount, 0, 255);
        if (clampedAmount == 0)
        {
            return WorldConstants.EmptyDensity;
        }

        int scaled = (int)math.round((clampedAmount / 255f) * WorldConstants.FullDensity);
        return (sbyte)math.clamp(scaled, 0, WorldConstants.FullDensity);
    }

    public static float SampleSurfaceHeight(TerrainGenerationSettings settings, int worldX, int worldZ)
    {
        return SampleSurfaceHeight(settings, (float)worldX, (float)worldZ);
    }

    public static float SampleSurfaceHeight(TerrainGenerationSettings settings, float worldX, float worldZ)
    {
        return math.max(
            SampleRockHeight(settings, worldX, worldZ),
            SampleDirtHeight(settings, worldX, worldZ));
    }

    public static float SampleRockHeight(TerrainGenerationSettings settings, int worldX, int worldZ)
    {
        return SampleRockHeight(settings, (float)worldX, (float)worldZ);
    }

    public static float SampleRockHeight(TerrainGenerationSettings settings, float worldX, float worldZ)
    {
        float normalizedNoise = SampleFractalNoise(settings, worldX, worldZ, GetDerivedNoiseOffset(settings.Seed, 1u));
        return settings.BaseRockHeight + normalizedNoise * settings.HeightAmplitude;
    }

    public static float SampleDirtHeight(TerrainGenerationSettings settings, int worldX, int worldZ)
    {
        return SampleDirtHeight(settings, (float)worldX, (float)worldZ);
    }

    public static float SampleDirtHeight(TerrainGenerationSettings settings, float worldX, float worldZ)
    {
        float normalizedNoise = SampleFractalNoise(settings, worldX, worldZ, GetDerivedNoiseOffset(settings.Seed, 2u));
        return settings.BaseDirtHeight + normalizedNoise * settings.HeightAmplitude;
    }

    private static float SampleFractalNoise(TerrainGenerationSettings settings, float worldX, float worldZ, float2 offset)
    {
        int octaves = math.max(1, settings.Octaves);
        float lacunarity = math.max(1f, settings.Lacunarity);
        float persistence = math.clamp(settings.Persistence, 0.0001f, 1f);

        float amplitude = 1f;
        float frequency = 1f;
        float total = 0f;
        float amplitudeSum = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float2 noiseCoord = new float2(worldX, worldZ) * settings.NoiseScale * frequency + offset;
            total += noise.cnoise(noiseCoord) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        if (amplitudeSum <= 0.0001f)
        {
            return 0.5f;
        }

        float normalizedNoise = total / amplitudeSum;
        return math.saturate(normalizedNoise * 0.5f + 0.5f);
    }

    private static float2 GetDerivedNoiseOffset(int seed, uint salt)
    {
        uint baseSeed = (uint)seed;
        uint xHash = Hash(baseSeed ^ (salt * 0x9E3779B9u));
        uint yHash = Hash(baseSeed ^ (salt * 0x85EBCA6Bu) ^ 0xC2B2AE35u);
        return new float2(
            HashToOffset(xHash),
            HashToOffset(yHash));
    }

    private static uint Hash(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }

    private static float HashToOffset(uint value)
    {
        return ((value & 0x00FFFFFFu) / 16777215f) * 10000f - 5000f;
    }
}
