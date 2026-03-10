using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Job에서 생성한 메쉬 데이터를 Unity Mesh 객체에 반영하는 유틸리티이다.
/// </summary>
public static class MeshApplyUtility
{
    private static readonly VertexAttributeDescriptor[] VertexLayout =
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4, 2)
    };

    public static void ApplyToSubChunk(SubChunkView view, SubChunkMeshData meshData, bool applyCollider)
    {
        Mesh mesh = view.EnsureMeshCreated();

        if (!meshData.IsCreated || meshData.Vertices.Length == 0 || meshData.Indices.Length == 0)
        {
            view.ClearMesh();
            return;
        }

        mesh.Clear();
        mesh.SetVertexBufferParams(meshData.Vertices.Length, VertexLayout);
        mesh.SetVertexBufferData(meshData.Vertices.Reinterpret<Vector3>(UnsafeUtility.SizeOf<float3>()), 0, 0, meshData.Vertices.Length, 0, MeshUpdateFlags.DontRecalculateBounds);
        mesh.SetVertexBufferData(meshData.Normals.Reinterpret<Vector3>(UnsafeUtility.SizeOf<float3>()), 0, 0, meshData.Normals.Length, 1, MeshUpdateFlags.DontRecalculateBounds);
        mesh.SetVertexBufferData(meshData.MaterialInfo.Reinterpret<Vector4>(UnsafeUtility.SizeOf<float4>()), 0, 0, meshData.MaterialInfo.Length, 2, MeshUpdateFlags.DontRecalculateBounds);
        mesh.SetIndexBufferParams(meshData.Indices.Length, IndexFormat.UInt32);
        mesh.SetIndexBufferData(meshData.Indices, 0, 0, meshData.Indices.Length, MeshUpdateFlags.DontRecalculateBounds);
        mesh.subMeshCount = 1;
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, meshData.Indices.Length, MeshTopology.Triangles), MeshUpdateFlags.DontRecalculateBounds);
        mesh.RecalculateBounds();
        view.RefreshWireframeMesh();

        if (view.MeshCollider != null)
        {
            view.MeshCollider.sharedMesh = null;
        }

        if (!applyCollider || view.MeshCollider == null)
        {
            return;
        }

        view.MeshCollider.sharedMesh = mesh;
    }

    public static void ApplyToProxy(ProxyChunkView view, SubChunkMeshData meshData)
    {
        Mesh mesh = view.EnsureMeshCreated();

        if (!meshData.IsCreated || meshData.Vertices.Length == 0 || meshData.Indices.Length == 0)
        {
            view.ClearMesh();
            return;
        }

        mesh.Clear();
        mesh.SetVertexBufferParams(meshData.Vertices.Length, VertexLayout);
        mesh.SetVertexBufferData(meshData.Vertices.Reinterpret<Vector3>(UnsafeUtility.SizeOf<float3>()), 0, 0, meshData.Vertices.Length, 0, MeshUpdateFlags.DontRecalculateBounds);
        mesh.SetVertexBufferData(meshData.Normals.Reinterpret<Vector3>(UnsafeUtility.SizeOf<float3>()), 0, 0, meshData.Normals.Length, 1, MeshUpdateFlags.DontRecalculateBounds);
        mesh.SetVertexBufferData(meshData.MaterialInfo.Reinterpret<Vector4>(UnsafeUtility.SizeOf<float4>()), 0, 0, meshData.MaterialInfo.Length, 2, MeshUpdateFlags.DontRecalculateBounds);
        mesh.SetIndexBufferParams(meshData.Indices.Length, IndexFormat.UInt32);
        mesh.SetIndexBufferData(meshData.Indices, 0, 0, meshData.Indices.Length, MeshUpdateFlags.DontRecalculateBounds);
        mesh.subMeshCount = 1;
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, meshData.Indices.Length, MeshTopology.Triangles), MeshUpdateFlags.DontRecalculateBounds);
        mesh.RecalculateBounds();
        view.RefreshWireframeMesh();
    }
}
