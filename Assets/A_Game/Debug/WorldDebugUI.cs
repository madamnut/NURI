using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>
/// Runtime debug overlay and translucent chunk boundary renderer.
/// F3 toggles the text overlay, and F3+G toggles 3x3 chunk grid boundary planes.
/// </summary>
public sealed class WorldDebugUI : MonoBehaviour
{
    private enum LodBoundaryDisplayMode
    {
        None = 0,
        Lod0ToLod1 = 1,
        Lod1ToLod2 = 2,
        Lod2OuterEdge = 3
    }

    [Header("References")]
    [SerializeField] private WorldSystem _worldSystem;
    [SerializeField] private Camera _referenceCamera;
    [SerializeField] private VoxelEditController _voxelEditController;
    [SerializeField] private GameObject _debugRoot;
    [FormerlySerializedAs("_targetText")]
    [SerializeField] private TMP_Text _leftText;
    [SerializeField] private TMP_Text _rightText;

    [Header("Overlay")]
    [SerializeField] private float _refreshInterval = 0.25f;

    [Header("Bounds")]
    [SerializeField] private Color _chunkBoundsColor = new Color(0.2f, 0.8f, 1f, 0.12f);
    [SerializeField] private Color _lod0ToLod1BoundsColor = new Color(0.35f, 1f, 0.35f, 0.12f);
    [SerializeField] private Color _lod1ToLod2BoundsColor = new Color(1f, 0.85f, 0.25f, 0.12f);
    [SerializeField] private Color _lod2OuterBoundsColor = new Color(1f, 0.35f, 0.35f, 0.12f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private int _frameCount;
    private float _elapsedTime;
    private int _currentFps;
    private bool _isDebugVisible;
    private bool _areChunkBoundsVisible;
    private bool _isF3Held;
    private bool _consumedF3Chord;
    private LodBoundaryDisplayMode _lodBoundaryMode;
    private Material _surfaceMaterial;
    private Mesh _quadMesh;
    private string _currentTargetLabel = "Target: Air";

    private void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        ApplyDebugVisibility(true);
        _areChunkBoundsVisible = false;
        _lodBoundaryMode = LodBoundaryDisplayMode.None;
    }

    private void OnEnable()
    {
        _frameCount = 0;
        _elapsedTime = 0f;
        _currentFps = 0;
        RefreshText();
    }

    private void Update()
    {
        HandleDebugShortcuts();

        if (!_isDebugVisible || (_leftText == null && _rightText == null))
        {
            return;
        }

        _currentTargetLabel = BuildTargetLabel();
        UpdateDisplayedText();

        _frameCount++;
        _elapsedTime += Time.unscaledDeltaTime;

        if (_elapsedTime >= Mathf.Max(0.01f, _refreshInterval))
        {
            _currentFps = Mathf.RoundToInt(_frameCount / _elapsedTime);
            _frameCount = 0;
            _elapsedTime = 0f;
            UpdateDisplayedText();
        }
    }

    private void OnRenderObject()
    {
        if ((!_areChunkBoundsVisible && _lodBoundaryMode == LodBoundaryDisplayMode.None) || _worldSystem == null)
        {
            return;
        }

        if (!EnsureSurfaceResources())
        {
            return;
        }

        if (_areChunkBoundsVisible)
        {
            if (!TryGetFocusCameraPosition(out Vector3 focusPosition))
            {
                return;
            }

            ChunkCoord centerCoord = WorldMath.WorldCellToChunkCoord(
                Mathf.FloorToInt(focusPosition.x),
                Mathf.FloorToInt(focusPosition.z));

            int regionStartX = (centerCoord.X - 1) * WorldConstants.ChunkSizeX;
            int regionStartZ = (centerCoord.Z - 1) * WorldConstants.ChunkSizeZ;
            int regionSpan = WorldConstants.ChunkSizeX * 3;

            for (int i = 0; i <= 3; i++)
            {
                DrawVerticalXPlane(regionStartX + i * WorldConstants.ChunkSizeX, regionStartZ, regionSpan, _chunkBoundsColor);
                DrawVerticalZPlane(regionStartZ + i * WorldConstants.ChunkSizeZ, regionStartX, regionSpan, _chunkBoundsColor);
            }
        }

        if (_lodBoundaryMode != LodBoundaryDisplayMode.None)
        {
            DrawLodBoundary();
        }
    }

    private void OnDestroy()
    {
        if (_surfaceMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_surfaceMaterial);
            }
            else
            {
                DestroyImmediate(_surfaceMaterial);
            }
        }

        if (_quadMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_quadMesh);
            }
            else
            {
                DestroyImmediate(_quadMesh);
            }
        }
    }

    private void RefreshText()
    {
        _currentTargetLabel = BuildTargetLabel();
        UpdateDisplayedText();
    }

    private void UpdateDisplayedText()
    {
        if (_leftText == null && _rightText == null)
        {
            return;
        }

        int activeChunks = _worldSystem != null ? _worldSystem.ActiveChunkCount : 0;
        int loadedChunks = _worldSystem != null ? _worldSystem.LoadedChunkCount : 0;
        int pendingLoads = _worldSystem != null ? _worldSystem.PendingChunkLoadCount : 0;
        int pendingGenerations = _worldSystem != null ? _worldSystem.PendingGenerationCount : 0;
        int pendingMeshBuilds = _worldSystem != null ? _worldSystem.PendingMeshBuildCount : 0;
        int pendingUnloads = _worldSystem != null ? _worldSystem.PendingChunkUnloadCount : 0;
        int pooledViews = _worldSystem != null ? _worldSystem.PooledChunkViewCount : 0;
        string managedMemory = FormatBytes(GC.GetTotalMemory(false));
        string allocatedMemory = FormatBytes(Profiler.GetTotalAllocatedMemoryLong());
        string reservedMemory = FormatBytes(Profiler.GetTotalReservedMemoryLong());
        string leftText =
            $"FPS: {_currentFps}\n" +
            $"{_currentTargetLabel}\n" +
            $"ChunkBounds: {(_areChunkBoundsVisible ? "ON" : "OFF")}\n" +
            $"LodBounds: {GetLodBoundaryModeLabel()}\n" +
            $"Seed: {(_worldSystem != null ? _worldSystem.GenerationSeed : 0)}";

        string rightText =
            $"ActiveChunks: {activeChunks}\n" +
            $"Lod0Chunks: {loadedChunks}\n" +
            $"QueuedLoads: {pendingLoads}\n" +
            $"PendingGenerations: {pendingGenerations}\n" +
            $"PendingMeshBuilds: {pendingMeshBuilds}\n" +
            $"PendingUnloads: {pendingUnloads}\n" +
            $"PooledViews: {pooledViews}\n" +
            $"ManagedMemory: {managedMemory}\n" +
            $"AllocatedMemory: {allocatedMemory}\n" +
            $"ReservedMemory: {reservedMemory}";

        if (_leftText != null)
        {
            _leftText.text = leftText;
        }

        if (_rightText != null)
        {
            _rightText.text = rightText;
        }
        else if (_leftText != null)
        {
            _leftText.text = $"{leftText}\n{rightText}";
        }
    }

    private string BuildTargetLabel()
    {
        Camera targetCamera = _referenceCamera != null ? _referenceCamera : Camera.main;
        if (_worldSystem == null || targetCamera == null)
        {
            return "Target: Air";
        }

        float maxDistance = _voxelEditController != null ? _voxelEditController.MaxRayDistance : 500f;
        LayerMask hitMask = _voxelEditController != null ? _voxelEditController.HitMask : ~0;

        Ray ray = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(0.1f, maxDistance), hitMask, QueryTriggerInteraction.Ignore))
        {
            return "Target: Air";
        }

        Vector3 insidePoint = hit.point - hit.normal * 0.01f;
        int cellX = Mathf.FloorToInt(insidePoint.x);
        int cellY = Mathf.FloorToInt(insidePoint.y);
        int cellZ = Mathf.FloorToInt(insidePoint.z);

        if (!_worldSystem.TryGetWorldCellMaterial(cellX, cellY, cellZ, out byte materialId) || materialId == 0)
        {
            return "Target: Air";
        }

        TerrainMaterialLibrary materialLibrary = _worldSystem.TerrainMaterialLibrary;
        string materialName = materialLibrary != null
            ? materialLibrary.GetDisplayName(materialId)
            : $"Material {materialId}";

        return $"Target: {materialName} (ID: {materialId})";
    }

    private void HandleDebugShortcuts()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (!_isF3Held && keyboard.f3Key.wasPressedThisFrame)
        {
            _isF3Held = true;
            _consumedF3Chord = false;
        }

        if (_isF3Held && keyboard.gKey.wasPressedThisFrame)
        {
            _areChunkBoundsVisible = !_areChunkBoundsVisible;
            _consumedF3Chord = true;
            RefreshText();
        }

        if (_isF3Held && keyboard.lKey.wasPressedThisFrame)
        {
            _lodBoundaryMode = GetNextLodBoundaryMode(_lodBoundaryMode);
            _consumedF3Chord = true;
            RefreshText();
        }

        if (_isF3Held && keyboard.f3Key.wasReleasedThisFrame)
        {
            if (!_consumedF3Chord)
            {
                ApplyDebugVisibility(!_isDebugVisible);
            }

            _isF3Held = false;
            _consumedF3Chord = false;
        }
    }

    private void ApplyDebugVisibility(bool visible)
    {
        _isDebugVisible = visible;

        if (_debugRoot != null)
        {
            _debugRoot.SetActive(visible);
        }
        else
        {
            if (_leftText != null)
            {
                _leftText.enabled = visible;
            }

            if (_rightText != null)
            {
                _rightText.enabled = visible;
            }
        }

        if (visible)
        {
            RefreshText();
        }
    }

    private void DrawLodBoundary()
    {
        if (_worldSystem == null || !_worldSystem.TryGetStreamingCenter(out ChunkCoord centerCoord))
        {
            return;
        }

        int baseRadius = _worldSystem.LoadRadius;
        int chunkRadius;
        Color color;

        switch (_lodBoundaryMode)
        {
            case LodBoundaryDisplayMode.Lod0ToLod1:
                chunkRadius = baseRadius;
                color = _lod0ToLod1BoundsColor;
                break;
            case LodBoundaryDisplayMode.Lod1ToLod2:
                chunkRadius = baseRadius * 2;
                color = _lod1ToLod2BoundsColor;
                break;
            case LodBoundaryDisplayMode.Lod2OuterEdge:
                chunkRadius = baseRadius * 3;
                color = _lod2OuterBoundsColor;
                break;
            default:
                return;
        }

        int minChunkX = centerCoord.X - chunkRadius;
        int maxChunkXExclusive = centerCoord.X + chunkRadius + 1;
        int minChunkZ = centerCoord.Z - chunkRadius;
        int maxChunkZExclusive = centerCoord.Z + chunkRadius + 1;

        int worldMinX = minChunkX * WorldConstants.ChunkSizeX;
        int worldMaxX = maxChunkXExclusive * WorldConstants.ChunkSizeX;
        int worldMinZ = minChunkZ * WorldConstants.ChunkSizeZ;
        int worldMaxZ = maxChunkZExclusive * WorldConstants.ChunkSizeZ;
        int spanX = worldMaxX - worldMinX;
        int spanZ = worldMaxZ - worldMinZ;

        DrawVerticalXPlane(worldMinX, worldMinZ, spanZ, color);
        DrawVerticalXPlane(worldMaxX, worldMinZ, spanZ, color);
        DrawVerticalZPlane(worldMinZ, worldMinX, spanX, color);
        DrawVerticalZPlane(worldMaxZ, worldMinX, spanX, color);
    }

    private bool EnsureSurfaceResources()
    {
        if (_quadMesh == null)
        {
            _quadMesh = CreateQuadMesh();
        }

        if (_surfaceMaterial != null)
        {
            return true;
        }

        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color") ??
            Shader.Find("Sprites/Default");

        if (shader == null)
        {
            return false;
        }

        _surfaceMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Transparent
        };

        _surfaceMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _surfaceMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _surfaceMaterial.SetInt("_Cull", (int)CullMode.Off);
        _surfaceMaterial.SetInt("_ZWrite", 0);
        _surfaceMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        _surfaceMaterial.SetFloat("_Surface", 1f);

        return true;
    }

    private bool TryGetFocusCameraPosition(out Vector3 position)
    {
        Camera targetCamera = Camera.current != null ? Camera.current : (_referenceCamera != null ? _referenceCamera : Camera.main);
        if (targetCamera == null)
        {
            position = default;
            return false;
        }

        position = targetCamera.transform.position;
        return true;
    }

    private void DrawVerticalXPlane(int worldX, int worldStartZ, int spanZ, Color color)
    {
        Matrix4x4 matrix = BuildPlaneMatrix(
            new Vector3(worldX, 0f, worldStartZ),
            Vector3.forward * spanZ,
            Vector3.up * WorldConstants.ChunkSizeY,
            Vector3.right);

        DrawPlane(matrix, color);
    }

    private void DrawVerticalZPlane(int worldZ, int worldStartX, int spanX, Color color)
    {
        Matrix4x4 matrix = BuildPlaneMatrix(
            new Vector3(worldStartX, 0f, worldZ),
            Vector3.right * spanX,
            Vector3.up * WorldConstants.ChunkSizeY,
            Vector3.forward);

        DrawPlane(matrix, color);
    }

    private void DrawPlane(Matrix4x4 matrix, Color color)
    {
        if (_surfaceMaterial == null || _quadMesh == null)
        {
            return;
        }

        _surfaceMaterial.SetColor(BaseColorId, color);
        _surfaceMaterial.SetColor(ColorId, color);
        _surfaceMaterial.SetPass(0);
        Graphics.DrawMeshNow(_quadMesh, matrix);
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "DebugBoundaryQuad"
        };

        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(1f, 1f, 0f)
        });

        mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
        mesh.SetNormals(new List<Vector3>
        {
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
            Vector3.forward
        });
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Matrix4x4 BuildPlaneMatrix(Vector3 origin, Vector3 axisX, Vector3 axisY, Vector3 normal)
    {
        Matrix4x4 matrix = Matrix4x4.identity;
        matrix.SetColumn(0, new Vector4(axisX.x, axisX.y, axisX.z, 0f));
        matrix.SetColumn(1, new Vector4(axisY.x, axisY.y, axisY.z, 0f));
        matrix.SetColumn(2, new Vector4(normal.x, normal.y, normal.z, 0f));
        matrix.SetColumn(3, new Vector4(origin.x, origin.y, origin.z, 1f));
        return matrix;
    }

    private static LodBoundaryDisplayMode GetNextLodBoundaryMode(LodBoundaryDisplayMode mode)
    {
        switch (mode)
        {
            case LodBoundaryDisplayMode.None:
                return LodBoundaryDisplayMode.Lod0ToLod1;
            case LodBoundaryDisplayMode.Lod0ToLod1:
                return LodBoundaryDisplayMode.Lod1ToLod2;
            case LodBoundaryDisplayMode.Lod1ToLod2:
                return LodBoundaryDisplayMode.Lod2OuterEdge;
            default:
                return LodBoundaryDisplayMode.None;
        }
    }

    private string GetLodBoundaryModeLabel()
    {
        switch (_lodBoundaryMode)
        {
            case LodBoundaryDisplayMode.Lod0ToLod1:
                return "LOD0-1";
            case LodBoundaryDisplayMode.Lod1ToLod2:
                return "LOD1-2";
            case LodBoundaryDisplayMode.Lod2OuterEdge:
                return "LOD2-END";
            default:
                return "OFF";
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double kilo = 1024d;
        const double mega = kilo * 1024d;
        const double giga = mega * 1024d;

        if (bytes >= giga)
        {
            return $"{bytes / giga:F2} GB";
        }

        if (bytes >= mega)
        {
            return $"{bytes / mega:F1} MB";
        }

        if (bytes >= kilo)
        {
            return $"{bytes / kilo:F1} KB";
        }

        return $"{bytes} B";
    }
}
