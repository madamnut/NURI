using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Runtime debug overlay and translucent chunk boundary renderer.
/// F3 toggles the text overlay, and F3+G toggles 3x3 chunk grid boundary planes.
/// </summary>
public sealed class WorldDebugUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WorldSystem _worldSystem;
    [SerializeField] private Camera _referenceCamera;
    [SerializeField] private GameObject _debugRoot;
    [SerializeField] private TMP_Text _targetText;

    [Header("Overlay")]
    [SerializeField] private float _refreshInterval = 0.25f;

    [Header("Bounds")]
    [SerializeField] private Color _chunkBoundsColor = new Color(0.2f, 0.8f, 1f, 0.12f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private int _frameCount;
    private float _elapsedTime;
    private int _currentFps;
    private bool _isDebugVisible;
    private bool _areChunkBoundsVisible;
    private bool _isF3Held;
    private bool _consumedF3Chord;
    private Material _surfaceMaterial;
    private Mesh _quadMesh;

    private void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        ApplyDebugVisibility(false);
        _areChunkBoundsVisible = false;
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

        if (!_isDebugVisible || _targetText == null)
        {
            return;
        }

        _frameCount++;
        _elapsedTime += Time.unscaledDeltaTime;

        if (_elapsedTime >= Mathf.Max(0.01f, _refreshInterval))
        {
            _currentFps = Mathf.RoundToInt(_frameCount / _elapsedTime);
            _frameCount = 0;
            _elapsedTime = 0f;
            RefreshText();
        }
    }

    private void OnRenderObject()
    {
        if (!_areChunkBoundsVisible || _worldSystem == null)
        {
            return;
        }

        if (!EnsureSurfaceResources())
        {
            return;
        }

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
        if (_targetText == null)
        {
            return;
        }

        int loadedChunks = _worldSystem != null ? _worldSystem.LoadedChunkCount : 0;
        _targetText.text =
            $"FPS: {_currentFps}\n" +
            $"LoadedChunks: {loadedChunks}\n" +
            $"ChunkBounds: {(_areChunkBoundsVisible ? "ON" : "OFF")}";
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
        else if (_targetText != null)
        {
            _targetText.enabled = visible;
        }

        if (visible)
        {
            RefreshText();
        }
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
}
