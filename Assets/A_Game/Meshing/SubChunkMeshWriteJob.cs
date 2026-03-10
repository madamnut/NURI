using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Count 단계에서 계산한 triangle 수와 offset을 바탕으로
/// 실제 버텍스와 인덱스를 출력 버퍼에 기록하는 Job이다.
///
/// 이 Job은 count 단계와 완전히 같은 규칙으로 동작해야 한다.
/// 코너 순서, cube index 비트 순서, edge 보간 규칙, 퇴화 삼각형 제거 조건이
/// 하나라도 어긋나면 triangle 개수와 offset이 맞지 않아
/// 구멍, 뒤틀린 삼각형, MeshCollider cooking 오류가 발생한다.
/// </summary>
 [BurstCompile]
public struct SubChunkMeshWriteJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<sbyte> Density;
    [ReadOnly] public NativeArray<byte> MaterialIds;
    [ReadOnly] public NativeArray<byte> TriangleCounts;
    [ReadOnly] public NativeArray<int> TriangleOffsets;

    public int SubChunkIndex;
    public float BlendDeadZoneMin;
    public float BlendDeadZoneMax;

    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<float3> Vertices;

    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<float3> Normals;

    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<int> Indices;

    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<float4> MaterialInfo;

    public void Execute(int index)
    {
        if (TriangleCounts[index] == 0)
        {
            return;
        }

        int localX = index % WorldConstants.SubChunkSize;
        int localZ = (index / WorldConstants.SubChunkSize) % WorldConstants.SubChunkSize;
        int localY = index / (WorldConstants.SubChunkSize * WorldConstants.SubChunkSize);
        int sampleBaseY = WorldMath.SubChunkStartY(SubChunkIndex) + localY;

        // 샘플 프로젝트의 표준 Marching Cubes 코너 순서를 그대로 사용한다.
        float3 p0 = new float3(localX + 0, localY + 0, localZ + 0);
        float3 p1 = new float3(localX + 1, localY + 0, localZ + 0);
        float3 p2 = new float3(localX + 1, localY + 0, localZ + 1);
        float3 p3 = new float3(localX + 0, localY + 0, localZ + 1);
        float3 p4 = new float3(localX + 0, localY + 1, localZ + 0);
        float3 p5 = new float3(localX + 1, localY + 1, localZ + 0);
        float3 p6 = new float3(localX + 1, localY + 1, localZ + 1);
        float3 p7 = new float3(localX + 0, localY + 1, localZ + 1);

        sbyte d0 = ReadDensity(localX + 0, sampleBaseY + 0, localZ + 0);
        sbyte d1 = ReadDensity(localX + 1, sampleBaseY + 0, localZ + 0);
        sbyte d2 = ReadDensity(localX + 1, sampleBaseY + 0, localZ + 1);
        sbyte d3 = ReadDensity(localX + 0, sampleBaseY + 0, localZ + 1);
        sbyte d4 = ReadDensity(localX + 0, sampleBaseY + 1, localZ + 0);
        sbyte d5 = ReadDensity(localX + 1, sampleBaseY + 1, localZ + 0);
        sbyte d6 = ReadDensity(localX + 1, sampleBaseY + 1, localZ + 1);
        sbyte d7 = ReadDensity(localX + 0, sampleBaseY + 1, localZ + 1);

        int cubeIndex = 0;
        if (MarchingCubesCommon.IsInside(d0)) cubeIndex |= 1 << 0;
        if (MarchingCubesCommon.IsInside(d1)) cubeIndex |= 1 << 1;
        if (MarchingCubesCommon.IsInside(d2)) cubeIndex |= 1 << 2;
        if (MarchingCubesCommon.IsInside(d3)) cubeIndex |= 1 << 3;
        if (MarchingCubesCommon.IsInside(d4)) cubeIndex |= 1 << 4;
        if (MarchingCubesCommon.IsInside(d5)) cubeIndex |= 1 << 5;
        if (MarchingCubesCommon.IsInside(d6)) cubeIndex |= 1 << 6;
        if (MarchingCubesCommon.IsInside(d7)) cubeIndex |= 1 << 7;

        if (MarchingCubesTables.EdgeTable[cubeIndex] == 0)
        {
            return;
        }

        int triangleStart = TriangleOffsets[index];
        int localTriangleIndex = 0;
        int rowIndex = cubeIndex * MarchingCubesTables.TriangleTableStride;
        for (int i = 0; i < MarchingCubesTables.TriangleTableStride; i += 3)
        {
            int e0 = MarchingCubesTables.TriangleTable[rowIndex + i];
            if (e0 == -1)
            {
                break;
            }

            int e1 = MarchingCubesTables.TriangleTable[rowIndex + i + 1];
            int e2 = MarchingCubesTables.TriangleTable[rowIndex + i + 2];

            float3 v0 = InterpolateEdge(e0, p0, p1, p2, p3, p4, p5, p6, p7, d0, d1, d2, d3, d4, d5, d6, d7);
            float3 v1 = InterpolateEdge(e1, p0, p1, p2, p3, p4, p5, p6, p7, d0, d1, d2, d3, d4, d5, d6, d7);
            float3 v2 = InterpolateEdge(e2, p0, p1, p2, p3, p4, p5, p6, p7, d0, d1, d2, d3, d4, d5, d6, d7);

            if (!IsValidTriangle(v0, v1, v2))
            {
                continue;
            }

            int triangleIndex = triangleStart + localTriangleIndex;
            int vertexStart = triangleIndex * 3;
            float3 normal = math.normalize(math.cross(v1 - v0, v2 - v0));
            float4 materialInfo0 = BuildBlendInfoForEdge(e0, localX, sampleBaseY, localZ);
            float4 materialInfo1 = BuildBlendInfoForEdge(e1, localX, sampleBaseY, localZ);
            float4 materialInfo2 = BuildBlendInfoForEdge(e2, localX, sampleBaseY, localZ);

            Vertices[vertexStart + 0] = v0;
            Vertices[vertexStart + 1] = v1;
            Vertices[vertexStart + 2] = v2;

            Normals[vertexStart + 0] = normal;
            Normals[vertexStart + 1] = normal;
            Normals[vertexStart + 2] = normal;

            Indices[vertexStart + 0] = vertexStart + 0;
            Indices[vertexStart + 1] = vertexStart + 1;
            Indices[vertexStart + 2] = vertexStart + 2;

            MaterialInfo[vertexStart + 0] = materialInfo0;
            MaterialInfo[vertexStart + 1] = materialInfo1;
            MaterialInfo[vertexStart + 2] = materialInfo2;

            localTriangleIndex++;
        }
    }

    private sbyte ReadDensity(int sampleX, int sampleY, int sampleZ)
    {
        return Density[WorldMath.SampleIndex(sampleX, sampleY, sampleZ)];
    }

    private byte ReadMaterial(int cellX, int cellY, int cellZ)
    {
        return MaterialIds[WorldMath.CellIndex(cellX, cellY, cellZ)];
    }

    private float4 BuildBlendInfoForEdge(int edgeIndex, int cellX, int cellY, int cellZ)
    {
        float dirtWeight = 0f;
        float rockWeight = 0f;

        switch (edgeIndex)
        {
            case 0:
                AccumulateEdgeMaterial(cellX, cellY - 1, cellZ - 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY - 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ - 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                break;
            case 1:
                AccumulateEdgeMaterial(cellX, cellY - 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX + 1, cellY - 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX + 1, cellY, cellZ, ref dirtWeight, ref rockWeight);
                break;
            case 2:
                AccumulateEdgeMaterial(cellX, cellY - 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY - 1, cellZ + 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ + 1, ref dirtWeight, ref rockWeight);
                break;
            case 3:
                AccumulateEdgeMaterial(cellX - 1, cellY - 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY - 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX - 1, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                break;
            case 4:
                AccumulateEdgeMaterial(cellX, cellY, cellZ - 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY + 1, cellZ - 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY + 1, cellZ, ref dirtWeight, ref rockWeight);
                break;
            case 5:
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX + 1, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY + 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX + 1, cellY + 1, cellZ, ref dirtWeight, ref rockWeight);
                break;
            case 6:
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ + 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY + 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY + 1, cellZ + 1, ref dirtWeight, ref rockWeight);
                break;
            case 7:
                AccumulateEdgeMaterial(cellX - 1, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX - 1, cellY + 1, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY + 1, cellZ, ref dirtWeight, ref rockWeight);
                break;
            case 8:
                AccumulateEdgeMaterial(cellX - 1, cellY, cellZ - 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ - 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX - 1, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                break;
            case 9:
                AccumulateEdgeMaterial(cellX, cellY, cellZ - 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX + 1, cellY, cellZ - 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX + 1, cellY, cellZ, ref dirtWeight, ref rockWeight);
                break;
            case 10:
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX + 1, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ + 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX + 1, cellY, cellZ + 1, ref dirtWeight, ref rockWeight);
                break;
            case 11:
                AccumulateEdgeMaterial(cellX - 1, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX - 1, cellY, cellZ + 1, ref dirtWeight, ref rockWeight);
                AccumulateEdgeMaterial(cellX, cellY, cellZ + 1, ref dirtWeight, ref rockWeight);
                break;
        }

        float totalWeight = dirtWeight + rockWeight;
        if (totalWeight <= 0.0001f)
        {
            byte materialId = ReadMaterial(cellX, cellY, cellZ);
            float rockOnly = materialId == TerrainDensityUtility.RockMaterialId ? 1f : 0f;
            return new float4(TerrainDensityUtility.DirtMaterialId, TerrainDensityUtility.RockMaterialId, rockOnly, 0f);
        }

        float rockBlend = rockWeight / totalWeight;
        rockBlend = ApplyBlendDeadZone(rockBlend, BlendDeadZoneMin, BlendDeadZoneMax);
        return new float4(TerrainDensityUtility.DirtMaterialId, TerrainDensityUtility.RockMaterialId, rockBlend, 0f);
    }

    private void AccumulateEdgeMaterial(int cellX, int cellY, int cellZ, ref float dirtWeight, ref float rockWeight)
    {
        if (cellX < 0 || cellX >= WorldConstants.ChunkSizeX ||
            cellY < 0 || cellY >= WorldConstants.ChunkSizeY ||
            cellZ < 0 || cellZ >= WorldConstants.ChunkSizeZ)
        {
            return;
        }

        byte materialId = ReadMaterial(cellX, cellY, cellZ);
        if (materialId == TerrainDensityUtility.DirtMaterialId)
        {
            dirtWeight += 1f;
        }
        else if (materialId == TerrainDensityUtility.RockMaterialId)
        {
            rockWeight += 1f;
        }
    }

    private static bool IsValidTriangle(float3 a, float3 b, float3 c)
    {
        const float epsilon = 0.000001f;

        if (math.lengthsq(a - b) <= epsilon || math.lengthsq(a - c) <= epsilon || math.lengthsq(b - c) <= epsilon)
        {
            return false;
        }

        float3 normal = math.cross(b - a, c - a);
        return math.lengthsq(normal) > epsilon;
    }

    private static float ApplyBlendDeadZone(float weight, float min, float max)
    {
        if (weight <= min)
        {
            return 0f;
        }

        if (weight >= max)
        {
            return 1f;
        }

        if (max <= min)
        {
            return weight >= max ? 1f : 0f;
        }

        return math.unlerp(min, max, weight);
    }

    private static float3 InterpolateEdge(
        int edgeIndex,
        float3 p0, float3 p1, float3 p2, float3 p3, float3 p4, float3 p5, float3 p6, float3 p7,
        sbyte d0, sbyte d1, sbyte d2, sbyte d3, sbyte d4, sbyte d5, sbyte d6, sbyte d7)
    {
        switch (edgeIndex)
        {
            case 0: return MarchingCubesCommon.Interpolate(p0, p1, d0, d1);
            case 1: return MarchingCubesCommon.Interpolate(p1, p2, d1, d2);
            case 2: return MarchingCubesCommon.Interpolate(p2, p3, d2, d3);
            case 3: return MarchingCubesCommon.Interpolate(p3, p0, d3, d0);
            case 4: return MarchingCubesCommon.Interpolate(p4, p5, d4, d5);
            case 5: return MarchingCubesCommon.Interpolate(p5, p6, d5, d6);
            case 6: return MarchingCubesCommon.Interpolate(p6, p7, d6, d7);
            case 7: return MarchingCubesCommon.Interpolate(p7, p4, d7, d4);
            case 8: return MarchingCubesCommon.Interpolate(p0, p4, d0, d4);
            case 9: return MarchingCubesCommon.Interpolate(p1, p5, d1, d5);
            case 10: return MarchingCubesCommon.Interpolate(p2, p6, d2, d6);
            case 11: return MarchingCubesCommon.Interpolate(p3, p7, d3, d7);
            default: return p0;
        }
    }
}
