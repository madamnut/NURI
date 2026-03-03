using UnityEngine;

public sealed class SubChunk : MonoBehaviour
{
    [Header("서브청크 인덱스 (0~15)")]
    [Range(0, 15)]
    [SerializeField] private int _subIndex;

    [Header("옵션")]
    [SerializeField] private bool _useCollider;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private MeshCollider _meshCollider;

    private Mesh _mesh;

    public int SubIndex => _subIndex;

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        if (_useCollider)
            _meshCollider = GetComponent<MeshCollider>();

        _mesh = new Mesh { name = $"SubChunkMesh_{_subIndex:00}" };
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _meshFilter.sharedMesh = _mesh;
    }

    // 머티리얼 설정(초기엔 전부 동일 머티리얼 사용)
    public void SetMaterial(Material mat)
    {
        if (mat == null) return;
        _meshRenderer.sharedMaterial = mat;
    }

    // 메시 데이터 적용(메인 스레드에서 호출)
    // - vertices: 정점 목록
    // - triangles: 인덱스(3개씩 삼각형)
    // - normals: null이면 RecalculateNormals 사용
    public void ApplyMesh(Vector3[] vertices, int[] triangles, Vector3[] normals = null)
    {
        if (_mesh == null)
        {
            _mesh = new Mesh { name = $"SubChunkMesh_{_subIndex:00}" };
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _meshFilter.sharedMesh = _mesh;
        }

        _mesh.Clear();

        _mesh.vertices = vertices;
        _mesh.triangles = triangles;

        if (normals != null && normals.Length == vertices.Length)
            _mesh.normals = normals;
        else
            _mesh.RecalculateNormals();

        _mesh.RecalculateBounds();

        if (_useCollider)
        {
            if (_meshCollider == null) _meshCollider = GetComponent<MeshCollider>();
            if (_meshCollider != null)
            {
                _meshCollider.sharedMesh = null;
                _meshCollider.sharedMesh = _mesh;
            }
        }
    }
}