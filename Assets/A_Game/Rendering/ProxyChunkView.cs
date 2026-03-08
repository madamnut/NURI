using UnityEngine;

/// <summary>
/// Render-only proxy view for world-aligned LOD1/LOD2 regions.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ProxyChunkView : MonoBehaviour
{
    [SerializeField] private MeshFilter _meshFilter;
    [SerializeField] private MeshRenderer _meshRenderer;

    private Mesh _mesh;

    public MeshRenderer MeshRenderer => _meshRenderer;

    private void Reset()
    {
        CacheComponents();
    }

    private void Awake()
    {
        CacheComponents();
        EnsureMeshCreated();
    }

    public void SetProxyCoord(ProxyCoord coord)
    {
        transform.position = new Vector3(
            coord.X * WorldConstants.ChunkSizeX,
            0f,
            coord.Z * WorldConstants.ChunkSizeZ);
        gameObject.name = $"Proxy_L{coord.LodLevel}_{coord.X}_{coord.Z}";
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
    }
}
