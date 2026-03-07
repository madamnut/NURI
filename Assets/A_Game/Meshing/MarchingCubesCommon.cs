using Unity.Mathematics;

/// <summary>
/// 메싱 과정에서 반복적으로 사용하는 공통 계산 함수를 모아둔 클래스이다.
///
/// Count Job과 Write Job이 같은 규칙으로 셀을 해석해야
/// "삼각형 개수 계산"과 "실제 삼각형 쓰기" 결과가 정확히 일치한다.
/// </summary>
public static class MarchingCubesCommon
{
    public static bool IsInside(byte density)
    {
        return density >= WorldConstants.SurfaceThreshold;
    }

    public static int CountTrianglesForTetra(byte d0, byte d1, byte d2, byte d3)
    {
        int insideCount = 0;
        if (IsInside(d0)) insideCount++;
        if (IsInside(d1)) insideCount++;
        if (IsInside(d2)) insideCount++;
        if (IsInside(d3)) insideCount++;

        if (insideCount == 0 || insideCount == 4)
        {
            return 0;
        }

        return insideCount == 2 ? 2 : 1;
    }

    public static float3 Interpolate(float3 p0, float3 p1, byte d0, byte d1)
    {
        float iso = WorldConstants.SurfaceThreshold;
        float v0 = d0;
        float v1 = d1;

        if (math.abs(iso - v0) < 0.0001f)
        {
            return p0;
        }

        if (math.abs(iso - v1) < 0.0001f)
        {
            return p1;
        }

        float denom = v1 - v0;
        if (math.abs(denom) < 0.0001f)
        {
            return p0;
        }

        float t = (iso - v0) / denom;
        return math.lerp(p0, p1, t);
    }

    public static void OrientTriangle(ref float3 a, ref float3 b, ref float3 c, float3 desiredNormal)
    {
        float3 normal = math.cross(b - a, c - a);
        if (math.dot(normal, desiredNormal) < 0f)
        {
            float3 temp = b;
            b = c;
            c = temp;
        }
    }
}
