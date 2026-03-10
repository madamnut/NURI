using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Render-only proxy view for world-aligned LOD1/LOD2 regions.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ProxyChunkView : MonoBehaviour
{
    [SerializeField] private MeshFilter _meshFilter;
    [SerializeField] private MeshRenderer _meshRenderer;
    [SerializeField] private MeshFilter _wireMeshFilter;
    [SerializeField] private MeshRenderer _wireMeshRenderer;

    private Mesh _mesh;
    private Mesh _wireMesh;
    private readonly List<int> _wireIndices = new List<int>(512);
    private readonly HashSet<ulong> _wireEdgeSet = new HashSet<ulong>();

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

    private void OnDestroy()
    {
        DestroyOwnedMesh(ref _mesh);
        DestroyOwnedMesh(ref _wireMesh);
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

    public void SetWireframeState(bool solidVisible, bool wireVisible, Material wireMaterial)
    {
        CacheComponents();

        if (_meshRenderer != null)
        {
            _meshRenderer.enabled = solidVisible;
        }

        if (!wireVisible)
        {
            if (_wireMeshRenderer != null)
            {
                _wireMeshRenderer.enabled = false;
            }
            return;
        }

        EnsureWireframeComponents();
        if (wireMaterial != null)
        {
            _wireMeshRenderer.sharedMaterial = wireMaterial;
        }

        _wireMeshRenderer.enabled = true;
        RefreshWireframeMesh();
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
        if (_wireMesh != null)
        {
            _wireMesh.Clear();
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
    }

    public void RefreshWireframeMesh()
    {
        if (_wireMeshRenderer == null || !_wireMeshRenderer.enabled)
        {
            return;
        }

        Mesh sourceMesh = EnsureMeshCreated();
        Mesh wireMesh = EnsureWireMeshCreated();

        if (sourceMesh == null || sourceMesh.vertexCount == 0 || sourceMesh.subMeshCount == 0)
        {
            wireMesh.Clear();
            return;
        }

        int[] triangles = sourceMesh.GetIndices(0);
        Vector3[] vertices = sourceMesh.vertices;

        _wireIndices.Clear();
        _wireEdgeSet.Clear();

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            AddWireEdge(triangles[i], triangles[i + 1]);
            AddWireEdge(triangles[i + 1], triangles[i + 2]);
            AddWireEdge(triangles[i + 2], triangles[i]);
        }

        wireMesh.Clear();
        wireMesh.vertices = vertices;
        wireMesh.SetIndices(_wireIndices, MeshTopology.Lines, 0);
        wireMesh.RecalculateBounds();
        _wireMeshFilter.sharedMesh = wireMesh;
    }

    private void EnsureWireframeComponents()
    {
        if (_wireMeshFilter != null && _wireMeshRenderer != null)
        {
            return;
        }

        Transform child = transform.Find("Wireframe");
        if (child == null)
        {
            GameObject go = new GameObject("Wireframe");
            go.transform.SetParent(transform, false);
            child = go.transform;
        }

        _wireMeshFilter = child.GetComponent<MeshFilter>();
        if (_wireMeshFilter == null)
        {
            _wireMeshFilter = child.gameObject.AddComponent<MeshFilter>();
        }

        _wireMeshRenderer = child.GetComponent<MeshRenderer>();
        if (_wireMeshRenderer == null)
        {
            _wireMeshRenderer = child.gameObject.AddComponent<MeshRenderer>();
        }

        _wireMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _wireMeshRenderer.receiveShadows = false;
        _wireMeshRenderer.enabled = false;
    }

    private Mesh EnsureWireMeshCreated()
    {
        EnsureWireframeComponents();

        if (_wireMesh == null)
        {
            _wireMesh = new Mesh
            {
                name = $"{name}_WireRuntimeMesh"
            };
            _wireMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _wireMesh.MarkDynamic();
        }

        _wireMeshFilter.sharedMesh = _wireMesh;
        return _wireMesh;
    }

    private void AddWireEdge(int a, int b)
    {
        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);
        ulong key = ((ulong)(uint)min << 32) | (uint)max;
        if (!_wireEdgeSet.Add(key))
        {
            return;
        }

        _wireIndices.Add(a);
        _wireIndices.Add(b);
    }

    private void DestroyOwnedMesh(ref Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(mesh);
        }
        else
        {
            DestroyImmediate(mesh);
        }

        mesh = null;
    }
}
