using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 서브청크 하나의 각 셀이 실제로 몇 개의 유효한 삼각형을 만드는지 계산하는 Job이다.
///
/// 여기서 "유효한 삼각형"이란 tri table상 존재할 뿐 아니라,
/// 실제 보간 결과가 퇴화하지 않는 삼각형을 뜻한다.
/// 이 검사를 count 단계에서 같이 해야 write 단계의 고정 버퍼 크기가 정확해진다.
/// </summary>
public struct SubChunkTriangleCountJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<byte> Density;
    public int SubChunkIndex;

    [WriteOnly] public NativeArray<byte> TriangleCounts;

    public void Execute(int index)
    {
        int localX = index % WorldConstants.SubChunkSize;
        int localZ = (index / WorldConstants.SubChunkSize) % WorldConstants.SubChunkSize;
        int localY = index / (WorldConstants.SubChunkSize * WorldConstants.SubChunkSize);

        int sampleBaseY = WorldMath.SubChunkStartY(SubChunkIndex) + localY;

        float3 p0 = new float3(localX + 0, localY + 0, localZ + 0);
        float3 p1 = new float3(localX + 1, localY + 0, localZ + 0);
        float3 p2 = new float3(localX + 1, localY + 0, localZ + 1);
        float3 p3 = new float3(localX + 0, localY + 0, localZ + 1);
        float3 p4 = new float3(localX + 0, localY + 1, localZ + 0);
        float3 p5 = new float3(localX + 1, localY + 1, localZ + 0);
        float3 p6 = new float3(localX + 1, localY + 1, localZ + 1);
        float3 p7 = new float3(localX + 0, localY + 1, localZ + 1);

        byte d0 = ReadDensity(localX + 0, sampleBaseY + 0, localZ + 0);
        byte d1 = ReadDensity(localX + 1, sampleBaseY + 0, localZ + 0);
        byte d2 = ReadDensity(localX + 1, sampleBaseY + 0, localZ + 1);
        byte d3 = ReadDensity(localX + 0, sampleBaseY + 0, localZ + 1);
        byte d4 = ReadDensity(localX + 0, sampleBaseY + 1, localZ + 0);
        byte d5 = ReadDensity(localX + 1, sampleBaseY + 1, localZ + 0);
        byte d6 = ReadDensity(localX + 1, sampleBaseY + 1, localZ + 1);
        byte d7 = ReadDensity(localX + 0, sampleBaseY + 1, localZ + 1);

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
            TriangleCounts[index] = 0;
            return;
        }

        int triCount = 0;
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

            if (IsValidTriangle(v0, v1, v2))
            {
                triCount++;
            }
        }

        TriangleCounts[index] = (byte)triCount;
    }

    private byte ReadDensity(int sampleX, int sampleY, int sampleZ)
    {
        return Density[WorldMath.SampleIndex(sampleX, sampleY, sampleZ)];
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

    private static float3 InterpolateEdge(
        int edgeIndex,
        float3 p0, float3 p1, float3 p2, float3 p3, float3 p4, float3 p5, float3 p6, float3 p7,
        byte d0, byte d1, byte d2, byte d3, byte d4, byte d5, byte d6, byte d7)
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
