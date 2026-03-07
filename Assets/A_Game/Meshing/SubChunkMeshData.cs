using System;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// 서브청크 하나의 메싱 결과를 담는 컨테이너이다.
/// </summary>
public sealed class SubChunkMeshData : IDisposable
{
    public NativeArray<float3> Vertices;
    public NativeArray<int> Indices;

    public SubChunkMeshData(int triangleCount, Allocator allocator)
    {
        int elementCount = triangleCount * 3;
        Vertices = new NativeArray<float3>(elementCount, allocator);
        Indices = new NativeArray<int>(elementCount, allocator);
    }

    public bool IsCreated => Vertices.IsCreated && Indices.IsCreated;

    public void Dispose()
    {
        if (Vertices.IsCreated)
        {
            Vertices.Dispose();
        }

        if (Indices.IsCreated)
        {
            Indices.Dispose();
        }
    }
}
