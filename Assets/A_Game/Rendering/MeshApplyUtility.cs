using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Job에서 생성한 메쉬 데이터를 Unity Mesh 객체에 반영하는 유틸리티이다.
/// </summary>
public static class MeshApplyUtility
{
    public static void ApplyToSubChunk(SubChunkView view, SubChunkMeshData meshData, bool applyCollider)
    {
        Mesh mesh = view.EnsureMeshCreated();

        if (!meshData.IsCreated || meshData.Vertices.Length == 0 || meshData.Indices.Length == 0)
        {
            view.ClearMesh();
            return;
        }

        view.PrepareScratchBuffers(meshData.Vertices.Length, meshData.Indices.Length, meshData.MaterialInfo.Length);
        CopyVertices(meshData.Vertices, view.VertexScratch);
        CopyIndices(meshData.Indices, view.IndexScratch);
        CopyMaterialInfo(meshData.MaterialInfo, view.Uv2Scratch);

        mesh.Clear();
        mesh.SetVertices(view.VertexScratch);
        mesh.SetTriangles(view.IndexScratch, 0, false);
        mesh.SetUVs(1, view.Uv2Scratch);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (!applyCollider)
        {
            return;
        }

        view.MeshCollider.sharedMesh = null;
        view.MeshCollider.sharedMesh = mesh;
    }

    private static void CopyVertices(NativeArray<float3> source, System.Collections.Generic.List<Vector3> destination)
    {
        for (int i = 0; i < source.Length; i++)
        {
            destination.Add(source[i]);
        }
    }

    private static void CopyIndices(NativeArray<int> source, System.Collections.Generic.List<int> destination)
    {
        for (int i = 0; i < source.Length; i++)
        {
            destination.Add(source[i]);
        }
    }

    private static void CopyMaterialInfo(NativeArray<float2> source, System.Collections.Generic.List<Vector2> destination)
    {
        for (int i = 0; i < source.Length; i++)
        {
            float2 value = source[i];
            destination.Add(new Vector2(value.x, value.y));
        }
    }
}
