// Assets/A_Game/SubChunk.cs  (네 경로 기준)
// 교체 범위: 파일 전체

using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public sealed class SubChunk : MonoBehaviour
{
    [Header("서브청크 인덱스 (0~15)")]
    [Range(0, 15)]
    [SerializeField] private int _subIndex;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private MeshCollider _meshCollider;

    private Mesh _mesh;

    public int SubIndex => _subIndex;

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        _meshCollider = GetComponent<MeshCollider>();

        // 지형 콜라이더는 Convex=false
        _meshCollider.convex = false;

        _mesh = new Mesh { name = $"SubChunkMesh_{_subIndex:00}" };
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        _meshFilter.sharedMesh = _mesh;

        // 빈 메쉬를 콜라이더에 넣으면 PhysX가 실패할 수 있으니, 초기에는 null
        _meshCollider.sharedMesh = null;
    }

    public void SetMaterial(Material mat)
    {
        if (mat == null) return;
        _meshRenderer.sharedMaterial = mat;
    }

    public void ApplyMesh(Vector3[] vertices, int[] triangles, Vector3[] normals = null)
    {
        if (_mesh == null)
        {
            _mesh = new Mesh { name = $"SubChunkMesh_{_subIndex:00}" };
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _meshFilter.sharedMesh = _mesh;
        }

        _mesh.Clear();

        // 빈 메쉬면: 렌더도 비우고 콜라이더는 반드시 null로
        if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length == 0)
        {
            _meshCollider.sharedMesh = null;
            return;
        }

        _mesh.vertices = vertices;
        _mesh.triangles = triangles;

        if (normals != null && normals.Length == vertices.Length)
            _mesh.normals = normals;
        else
            _mesh.RecalculateNormals();

        _mesh.RecalculateBounds();

        // 콜라이더 갱신 (null -> mesh)
        _meshCollider.sharedMesh = null;
        _meshCollider.sharedMesh = _mesh;
    }
}