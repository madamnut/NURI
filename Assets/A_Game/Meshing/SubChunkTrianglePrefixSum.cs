using Unity.Collections;

/// <summary>
/// 삼각형 개수 배열을 누적합(prefix sum)으로 변환하는 유틸리티이다.
/// </summary>
public static class SubChunkTrianglePrefixSum
{
    public static int Build(NativeArray<byte> triangleCounts, NativeArray<int> triangleOffsets)
    {
        int total = 0;

        for (int i = 0; i < triangleCounts.Length; i++)
        {
            triangleOffsets[i] = total;
            total += triangleCounts[i];
        }

        return total;
    }
}
