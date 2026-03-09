using UnityEngine;

/// <summary>
/// 서브청크 하나의 렌더링과 충돌 메쉬를 담당하는 컴포넌트이다.
///
/// 월드 데이터는 이 클래스가 소유하지 않는다.
/// 외부에서 만들어진 메쉬 결과를 받아 MeshFilter / MeshCollider에 반영하는 역할만 담당한다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class SubChunkView : MonoBehaviour
{
    [SerializeField] private MeshFilter _meshFilter;
    [SerializeField] private MeshRenderer _meshRenderer;
    [SerializeField] private MeshCollider _meshCollider;

    private Mesh _mesh;

    public MeshRenderer MeshRenderer => _meshRenderer;
    public MeshCollider MeshCollider => _meshCollider;

    private void Reset()
    {
        CacheComponents();
    }

    private void Awake()
    {
        CacheComponents();
        EnsureMeshCreated();
        if (_meshCollider != null)
        {
            _meshCollider.convex = false;
        }
    }

    public void SetMaterial(Material material)
    {
        if (material != null)
        {
            _meshRenderer.sharedMaterial = material;
        }
    }

    public Mesh EnsureMeshCreated()
    {
        CacheComponents();

        if (_mesh == null)
        {
            _mesh = new Mesh
            {
                name = $"{name}_RuntimeMesh"
            };
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.MarkDynamic();
        }

        _meshFilter.sharedMesh = _mesh;
        return _mesh;
    }

    public void ClearMesh()
    {
        Mesh mesh = EnsureMeshCreated();
        mesh.Clear();
        if (_meshCollider != null)
        {
            _meshCollider.sharedMesh = null;
        }
    }

    private void CacheComponents()
    {
        if (_meshFilter == null)
        {
            _meshFilter = GetComponent<MeshFilter>();
        }

        if (_meshRenderer == null)
        {
            _meshRenderer = GetComponent<MeshRenderer>();
        }

        if (_meshCollider == null)
        {
            _meshCollider = GetComponent<MeshCollider>();
        }
    }
}
