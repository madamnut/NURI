// Assets/Scripts/World/MarchingCubesMesher.cs
// 새 파일

using System.Collections.Generic;
using UnityEngine;

public static class MarchingCubesMesher
{
    // 코너(8개) 표준 순서
    // 0:(0,0,0) 1:(1,0,0) 2:(1,0,1) 3:(0,0,1) 4:(0,1,0) 5:(1,1,0) 6:(1,1,1) 7:(0,1,1)
    private static readonly Vector3Int[] CornerOffset =
    {
        new Vector3Int(0,0,0),
        new Vector3Int(1,0,0),
        new Vector3Int(1,0,1),
        new Vector3Int(0,0,1),
        new Vector3Int(0,1,0),
        new Vector3Int(1,1,0),
        new Vector3Int(1,1,1),
        new Vector3Int(0,1,1),
    };

    // 엣지(12개) 표준: (코너 인덱스 쌍)
    private static readonly int[,] EdgeCorner =
    {
        {0,1},{1,2},{2,3},{3,0},
        {4,5},{5,6},{6,7},{7,4},
        {0,4},{1,5},{2,6},{3,7}
    };

    // iso=1 고정(너 설계)
    private const float Iso = 1f;

    /// <summary>
    /// 서브청크(sy:0..15) 하나의 메시를 생성한다.
    /// - ChunkColumnData는 코너 샘플(17x257x17, 1D)이다.
    /// - inside 판정: amount >= 1
    /// - 좌표계: 1유닛 = 1복셀(코너 간 거리 = 1)
    /// </summary>
    public static void BuildSubChunkMesh(
        ChunkColumnData data,
        int subIndex,
        List<Vector3> outVertices,
        List<int> outTriangles)
    {
        outVertices.Clear();
        outTriangles.Clear();

        // 서브청크의 셀 Y 범위(16개)
        int y0 = subIndex * Chunk.SubSizeY;          // 0,16,32,...,240
        int y1 = y0 + (Chunk.SubSizeY - 1);          // 15,31,...,255

        // 각 셀(큐브)을 순회: x 0..15, z 0..15, y y0..y1
        for (int y = y0; y <= y1; y++)
        {
            for (int z = 0; z < Chunk.SizeZ; z++)
            {
                for (int x = 0; x < Chunk.SizeX; x++)
                {
                    // 8 코너 샘플(amount) 읽기
                    float[] cornerValue = new float[8];
                    Vector3[] cornerPos = new Vector3[8];

                    for (int i = 0; i < 8; i++)
                    {
                        int sx = x + CornerOffset[i].x;
                        int sy = y + CornerOffset[i].y;
                        int sz = z + CornerOffset[i].z;

                        // amount: 0..128 (byte) -> float
                        cornerValue[i] = data.Get(sx, sy, sz);
                        cornerPos[i] = new Vector3(sx, sy, sz);
                    }

                    // cubeIndex 계산(inside면 bit=1)
                    int cubeIndex = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (cornerValue[i] >= Iso) cubeIndex |= (1 << i);
                    }

                    // 전부 공기 or 전부 고체면 스킵
                    int edgeMask = MarchingCubesTables.EdgeTable[cubeIndex];
                    if (edgeMask == 0) continue;

                    // 엣지 교차점(최대 12개)
                    Vector3[] vertList = new Vector3[12];

                    for (int e = 0; e < 12; e++)
                    {
                        if ((edgeMask & (1 << e)) == 0) continue;

                        int c0 = EdgeCorner[e, 0];
                        int c1 = EdgeCorner[e, 1];

                        Vector3 p0 = cornerPos[c0];
                        Vector3 p1 = cornerPos[c1];

                        float v0 = cornerValue[c0];
                        float v1 = cornerValue[c1];

                        vertList[e] = VertexInterp(Iso, p0, p1, v0, v1);
                    }

                    // TriTable을 따라 삼각형 생성
                    for (int t = 0; t < 16; t += 3)
                    {
                        int e0 = MarchingCubesTables.TriTable[cubeIndex, t];
                        if (e0 == -1) break;

                        int e1 = MarchingCubesTables.TriTable[cubeIndex, t + 1];
                        int e2 = MarchingCubesTables.TriTable[cubeIndex, t + 2];

                        int baseIndex = outVertices.Count;

                        outVertices.Add(vertList[e0]);
                        outVertices.Add(vertList[e1]);
                        outVertices.Add(vertList[e2]);

                        outTriangles.Add(baseIndex + 0);
                        outTriangles.Add(baseIndex + 1);
                        outTriangles.Add(baseIndex + 2);
                    }
                }
            }
        }
    }

    // iso = 1 기준으로 엣지 교차점 보간
    private static Vector3 VertexInterp(float iso, Vector3 p0, Vector3 p1, float v0, float v1)
    {
        // 극단 케이스 처리(분모 0 등)
        if (Mathf.Abs(iso - v0) < 0.00001f) return p0;
        if (Mathf.Abs(iso - v1) < 0.00001f) return p1;
        if (Mathf.Abs(v1 - v0) < 0.00001f) return p0;

        float t = (iso - v0) / (v1 - v0);
        return p0 + t * (p1 - p0);
    }
}