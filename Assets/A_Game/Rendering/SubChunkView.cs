using UnityEngine;
using System.Collections.Generic;

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
    [SerializeField] private MeshFilter _wireMeshFilter;
    [SerializeField] private MeshRenderer _wireMeshRenderer;

    private Mesh _mesh;
    private Mesh _wireMesh;
    private readonly List<int> _wireIndices = new List<int>(512);
    private readonly HashSet<ulong> _wireEdgeSet = new HashSet<ulong>();

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

    private void OnDestroy()
    {
        DestroyOwnedMesh(ref _mesh);
        DestroyOwnedMesh(ref _wireMesh);
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
        if (_meshCollider != null)
        {
            _meshCollider.sharedMesh = null;
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
