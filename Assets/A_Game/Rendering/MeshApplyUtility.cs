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

        mesh.Clear();
        mesh.vertices = ToVector3Array(meshData.Vertices);
        mesh.triangles = ToIntArray(meshData.Indices);
        mesh.uv2 = ToVector2Array(meshData.MaterialInfo);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (!applyCollider)
        {
            return;
        }

        view.MeshCollider.sharedMesh = null;
        view.MeshCollider.sharedMesh = mesh;
    }

    private static Vector3[] ToVector3Array(NativeArray<float3> source)
    {
        Vector3[] array = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            array[i] = source[i];
        }

        return array;
    }

    private static int[] ToIntArray(NativeArray<int> source)
    {
        int[] array = new int[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            array[i] = source[i];
        }

        return array;
    }

    private static Vector2[] ToVector2Array(NativeArray<float2> source)
    {
        Vector2[] array = new Vector2[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            float2 value = source[i];
            array[i] = new Vector2(value.x, value.y);
        }

        return array;
    }
}
