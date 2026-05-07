using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Owns chunk data, chunk views, streaming, generation, meshing, and density edits.
/// </summary>
public sealed class WorldSystem : MonoBehaviour
{
    public struct CellRaycastHit
    {
        public Vector3Int Cell;
        public Vector3Int PreviousCell;
        public bool HasPreviousCell;
        public byte MaterialId;
        public byte Amount;
        public float Distance;
    }

    public enum TerrainWireframeMode
    {
        Off = 0,
        Overlay = 1,
        WireOnly = 2
    }

    private const int LOD0 = 0;
    private const int LOD1 = 1;
    private const int LOD2 = 2;
    private enum InitialStreamingPhase
    {
        Lod0Only = 0,
        Proxy1 = 1,
        Proxy2 = 2,
        Complete = 3
    }

    [Header("Generation")]
    [SerializeField] private TerrainGenerationSettings _generationSettings = new TerrainGenerationSettings
    {
        NoiseScale = 0.02f,
        HeightAmplitude = 64f,
        Seed = 0,
        Octaves = 4,
        Lacunarity = 2f,
        Persistence = 0.5f,
        BaseDirtHeight = 70f,
        BaseRockHeight = 65f,
        SurfaceFade = 8f
    };

    [Header("Streaming")]
    [SerializeField] private Transform _streamingTarget;
    [SerializeField] private bool _finiteMap;
    [SerializeField] private int _finiteChunkDiameter = 64;
    [SerializeField] private int _baseChunkDiameter = 16;
    [SerializeField] private int _proxy1Border = 16;
    [SerializeField] private int _proxy2Border = 32;
    [SerializeField] private bool _renderProxyChunks = true;
    [SerializeField] private int _maxLod0LoadsPerFrame = 2;
    [SerializeField] private int _maxProxy1LoadsPerFrame = 1;
    [SerializeField] private int _maxProxy2LoadsPerFrame = 1;
    [SerializeField] private int _maxChunkUnloadsPerFrame = 4;
    [SerializeField] private int _maxLod0AppliesPerFrame = 2;
    [SerializeField] private int _maxProxy1AppliesPerFrame = 1;
    [SerializeField] private int _maxProxy2AppliesPerFrame = 1;

    [Header("View")]
    [SerializeField] private ChunkView _chunkViewPrefab;
    [SerializeField] private Transform _chunkRoot;
    [SerializeField] private Material _terrainMaterial;
    [SerializeField] private TerrainMaterialLibrary _terrainMaterialLibrary;
    [SerializeField] private bool _applyMeshCollider = true;
    [SerializeField, Min(0)] private int _maxChunkViewPoolCount = 256;

    [Header("Material Blending")]
    [SerializeField, Range(0f, 1f)] private float _blendDeadZoneMin = 0.2f;
    [SerializeField, Range(0f, 1f)] private float _blendDeadZoneMax = 0.8f;

    [Header("Debug")]
    [SerializeField] private Color _terrainWireframeColor = new Color(0.02f, 0.03f, 0.04f, 0.92f);

    private sealed class PendingChunkGeneration
    {
        public ChunkCoord Coord;
        public JobHandle Handle;
    }

    private sealed class PendingSubChunkBuild : IDisposable
    {
        public int SubChunkIndex;
        public int CellCount;
        public bool WriteScheduled;
        public bool ReadyToApply;
        public SubChunkView View;
        public NativeArray<byte> TriangleCounts;
        public NativeArray<int> TriangleOffsets;
        public SubChunkMeshData MeshData;
        public JobHandle Handle;

        public void Dispose()
        {
            if (TriangleCounts.IsCreated)
            {
                TriangleCounts.Dispose();
            }

            if (TriangleOffsets.IsCreated)
            {
                TriangleOffsets.Dispose();
            }

            if (MeshData != null)
            {
                MeshData.Dispose();
                MeshData = null;
            }
        }
    }

    private sealed class PendingChunkMeshBuild : IDisposable
    {
        public ChunkCoord Coord;
        public int TargetLod;
        public ChunkView View;
        public bool ApplyCollider;
        public bool HideUntilReady;
        public PendingSubChunkBuild[] SubChunkBuilds;

        public void Dispose()
        {
            if (SubChunkBuilds == null)
            {
                return;
            }

            for (int i = 0; i < SubChunkBuilds.Length; i++)
            {
                SubChunkBuilds[i]?.Dispose();
            }
        }
    }

    private sealed class PendingProxyMeshBuild : IDisposable
    {
        public ProxyCoord Coord;
        public ProxyChunkView View;
        public NativeArray<byte> TriangleCounts;
        public NativeArray<int> TriangleOffsets;
        public SubChunkMeshData MeshData;
        public JobHandle Handle;
        public bool WriteScheduled;

        public void Dispose()
        {
            if (TriangleCounts.IsCreated)
            {
                TriangleCounts.Dispose();
            }

            if (TriangleOffsets.IsCreated)
            {
                TriangleOffsets.Dispose();
            }

            if (MeshData != null)
            {
                MeshData.Dispose();
                MeshData = null;
            }
        }
    }

    private readonly ChunkDataStore _chunkStore = new ChunkDataStore();
    private readonly Dictionary<ChunkCoord, ChunkView> _chunkViews = new Dictionary<ChunkCoord, ChunkView>();
    private readonly Dictionary<ChunkCoord, int> _activeChunkLods = new Dictionary<ChunkCoord, int>();
    private readonly Dictionary<ProxyCoord, ProxyChunkView> _proxyViews = new Dictionary<ProxyCoord, ProxyChunkView>();
    private readonly HashSet<ChunkCoord> _modifiedChunks = new HashSet<ChunkCoord>();
    private readonly HashSet<ChunkCoord> _desiredChunkCoords = new HashSet<ChunkCoord>();
    private readonly Dictionary<ChunkCoord, int> _desiredChunkLods = new Dictionary<ChunkCoord, int>();
    private readonly HashSet<ProxyCoord> _desiredProxyCoords = new HashSet<ProxyCoord>();
    private readonly Queue<ChunkCoord> _pendingChunkLoads = new Queue<ChunkCoord>();
    private readonly Queue<ChunkCoord> _pendingChunkUnloads = new Queue<ChunkCoord>();
    private readonly HashSet<ChunkCoord> _pendingLoadSet = new HashSet<ChunkCoord>();
    private readonly HashSet<ChunkCoord> _pendingUnloadSet = new HashSet<ChunkCoord>();
    private readonly Queue<ProxyCoord> _pendingProxyLoads = new Queue<ProxyCoord>();
    private readonly Queue<ProxyCoord> _pendingProxyUnloads = new Queue<ProxyCoord>();
    private readonly HashSet<ProxyCoord> _pendingProxyLoadSet = new HashSet<ProxyCoord>();
    private readonly HashSet<ProxyCoord> _pendingProxyUnloadSet = new HashSet<ProxyCoord>();
    private readonly Dictionary<ChunkCoord, PendingChunkGeneration> _pendingGenerations = new Dictionary<ChunkCoord, PendingChunkGeneration>();
    private readonly Dictionary<ChunkCoord, PendingChunkMeshBuild> _pendingChunkMeshBuilds = new Dictionary<ChunkCoord, PendingChunkMeshBuild>();
    private readonly Dictionary<ProxyCoord, PendingProxyMeshBuild> _pendingProxyMeshBuilds = new Dictionary<ProxyCoord, PendingProxyMeshBuild>();
    private readonly List<ChunkCoord> _coordBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _loadBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _unloadBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _deferredUnloadBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _completedGenerationBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _completedChunkMeshBuffer = new List<ChunkCoord>();
    private readonly List<ProxyCoord> _proxyCoordBuffer = new List<ProxyCoord>();
    private readonly List<ProxyCoord> _proxyLoadBuffer = new List<ProxyCoord>();
    private readonly List<ProxyCoord> _proxyUnloadBuffer = new List<ProxyCoord>();
    private readonly List<ProxyCoord> _deferredProxyUnloadBuffer = new List<ProxyCoord>();
    private readonly List<ProxyCoord> _completedProxyMeshBuffer = new List<ProxyCoord>();
    private readonly Stack<ChunkView> _chunkViewPool = new Stack<ChunkView>();
    private readonly Stack<ProxyChunkView> _proxyViewPool = new Stack<ProxyChunkView>();
    private readonly HashSet<Vector3Int> _preExistingSolidCells = new HashSet<Vector3Int>();

    private bool _hasStreamingCenter;
    private ChunkCoord _streamingCenter;
    private InitialStreamingPhase _initialStreamingPhase;
    private Material _terrainWireframeMaterial;
    private TerrainWireframeMode _terrainWireframeMode;

    public int ActiveChunkCount => _chunkViews.Count + _proxyViews.Count;
    public int LoadedChunkCount => _chunkStore.Count;
    public int PendingChunkLoadCount => _pendingChunkLoads.Count + _pendingProxyLoads.Count;
    public int PendingGenerationCount => _pendingGenerations.Count;
    public int PendingMeshBuildCount => _pendingChunkMeshBuilds.Count + _pendingProxyMeshBuilds.Count;
    public int PendingChunkUnloadCount => _pendingChunkUnloads.Count + _pendingProxyUnloads.Count;
    public int PooledChunkViewCount => _chunkViewPool.Count + _proxyViewPool.Count;
    public bool HasGeneratedWorld { get; private set; }
    public TerrainMaterialLibrary TerrainMaterialLibrary => _terrainMaterialLibrary;
    public int GenerationSeed => _generationSettings.Seed;
    public bool FiniteMap => _finiteMap;
    public TerrainWireframeMode CurrentTerrainWireframeMode => _terrainWireframeMode;
    public string TerrainWireframeModeLabel => GetTerrainWireframeModeLabel(_terrainWireframeMode);
    public int FiniteChunkDiameter => Mathf.Max(1, _finiteChunkDiameter);
    public int BaseChunkDiameter => SanitizePositiveMultiple(_baseChunkDiameter, 4);
    public int Proxy1Border => SanitizePositiveMultiple(_proxy1Border, 2);
    public int Proxy2Border => SanitizePositiveMultiple(_proxy2Border, 4);
    public bool RenderProxyChunks => _renderProxyChunks && !_finiteMap;
    private int MaxChunkViewPoolCount => Mathf.Max(0, _maxChunkViewPoolCount);
    private float BlendDeadZoneMin => Mathf.Clamp01(Mathf.Min(_blendDeadZoneMin, _blendDeadZoneMax));
    private float BlendDeadZoneMax => Mathf.Clamp01(Mathf.Max(_blendDeadZoneMin, _blendDeadZoneMax));

    private void OnValidate()
    {
        _finiteChunkDiameter = Mathf.Max(1, _finiteChunkDiameter);
        _baseChunkDiameter = SanitizePositiveMultiple(_baseChunkDiameter, 4);
        _proxy1Border = SanitizePositiveMultiple(_proxy1Border, 2);
        _proxy2Border = SanitizePositiveMultiple(_proxy2Border, 4);
        if (_finiteMap)
        {
            _renderProxyChunks = false;
        }

        _maxLod0LoadsPerFrame = Mathf.Max(0, _maxLod0LoadsPerFrame);
        _maxProxy1LoadsPerFrame = Mathf.Max(0, _maxProxy1LoadsPerFrame);
        _maxProxy2LoadsPerFrame = Mathf.Max(0, _maxProxy2LoadsPerFrame);
        _maxChunkUnloadsPerFrame = Mathf.Max(0, _maxChunkUnloadsPerFrame);
        _maxLod0AppliesPerFrame = Mathf.Max(0, _maxLod0AppliesPerFrame);
        _maxProxy1AppliesPerFrame = Mathf.Max(0, _maxProxy1AppliesPerFrame);
        _maxProxy2AppliesPerFrame = Mathf.Max(0, _maxProxy2AppliesPerFrame);
    }

    [ContextMenu("Generate Initial World")]
    public void GenerateInitialWorld()
    {
        EnsureTerrainMaterialConfigured();
        ClearWorld();

        HasGeneratedWorld = true;
        _initialStreamingPhase = InitialStreamingPhase.Lod0Only;
        RefreshStreamingCenter(true);
        ProcessQueuedOperations();
        ProcessCompletedGenerationJobs();
        ProcessCompletedChunkMeshBuilds();
        ProcessCompletedProxyMeshBuilds();
        AdvanceInitialStreamingPhase();
    }

    [ContextMenu("Clear World")]
    public void ClearWorld()
    {
        CompleteAndDisposePendingJobs();

        foreach (ChunkView view in _chunkViews.Values)
        {
            if (view == null)
            {
                continue;
            }

            DestroyChunkView(view);
        }

        foreach (ProxyChunkView view in _proxyViews.Values)
        {
            if (view == null)
            {
                continue;
            }

            DestroyProxyView(view);
        }

        while (_chunkViewPool.Count > 0)
        {
            ChunkView pooledView = _chunkViewPool.Pop();
            if (pooledView != null)
            {
                DestroyChunkView(pooledView);
            }
        }

        while (_proxyViewPool.Count > 0)
        {
            ProxyChunkView pooledView = _proxyViewPool.Pop();
            if (pooledView != null)
            {
                DestroyProxyView(pooledView);
            }
        }

        _chunkViews.Clear();
        _activeChunkLods.Clear();
        _proxyViews.Clear();
        _chunkStore.ClearAndDispose();
        _desiredChunkCoords.Clear();
        _desiredChunkLods.Clear();
        _desiredProxyCoords.Clear();
        _pendingChunkLoads.Clear();
        _pendingChunkUnloads.Clear();
        _pendingLoadSet.Clear();
        _pendingUnloadSet.Clear();
        _pendingProxyLoads.Clear();
        _pendingProxyUnloads.Clear();
        _pendingProxyLoadSet.Clear();
        _pendingProxyUnloadSet.Clear();
        _coordBuffer.Clear();
        _loadBuffer.Clear();
        _unloadBuffer.Clear();
        _deferredUnloadBuffer.Clear();
        _completedGenerationBuffer.Clear();
        _completedChunkMeshBuffer.Clear();
        _proxyCoordBuffer.Clear();
        _proxyLoadBuffer.Clear();
        _proxyUnloadBuffer.Clear();
        _deferredProxyUnloadBuffer.Clear();
        _completedProxyMeshBuffer.Clear();
        _modifiedChunks.Clear();
        _hasStreamingCenter = false;
        _initialStreamingPhase = InitialStreamingPhase.Lod0Only;
        HasGeneratedWorld = false;
    }

    public bool TryGetChunk(ChunkCoord coord, out ChunkData chunk)
    {
        return _chunkStore.TryGet(coord, out chunk);
    }

    public bool TryGetWorldCellMaterial(int worldX, int worldY, int worldZ, out byte materialId)
    {
        materialId = 0;

        if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
        {
            return false;
        }

        ChunkCoord coord = WorldMath.WorldCellToChunkCoord(worldX, worldZ);
        if (!_chunkStore.TryGet(coord, out ChunkData chunk))
        {
            return false;
        }

        Vector3Int localCell = WorldMath.WorldCellToLocalCell(worldX, worldY, worldZ);
        if (localCell.x < 0 || localCell.x >= WorldConstants.ChunkSizeX ||
            localCell.z < 0 || localCell.z >= WorldConstants.ChunkSizeZ)
        {
            return false;
        }

        materialId = chunk.GetMaterialId(localCell.x, localCell.y, localCell.z);
        return true;
    }

    public bool TryGetWorldCellData(int worldX, int worldY, int worldZ, out byte materialId, out byte amount)
    {
        materialId = 0;
        amount = 0;

        if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
        {
            return false;
        }

        ChunkCoord coord = WorldMath.WorldCellToChunkCoord(worldX, worldZ);
        if (!_chunkStore.TryGet(coord, out ChunkData chunk))
        {
            return false;
        }

        Vector3Int localCell = WorldMath.WorldCellToLocalCell(worldX, worldY, worldZ);
        if (localCell.x < 0 || localCell.x >= WorldConstants.ChunkSizeX ||
            localCell.z < 0 || localCell.z >= WorldConstants.ChunkSizeZ)
        {
            return false;
        }

        materialId = chunk.GetMaterialId(localCell.x, localCell.y, localCell.z);
        amount = chunk.GetMaterialAmount(localCell.x, localCell.y, localCell.z);
        return true;
    }

    public bool TryGetWorldSampleDensityAt(int worldSampleX, int sampleY, int worldSampleZ, out sbyte density)
    {
        return TryGetWorldSampleDensity(worldSampleX, sampleY, worldSampleZ, out density);
    }

    public bool TryRaycastSolidCell(Ray ray, float maxDistance, out CellRaycastHit hit)
    {
        hit = default;

        Vector3 direction = ray.direction;
        float directionLength = direction.magnitude;
        if (directionLength <= 0.0001f)
        {
            return false;
        }

        direction /= directionLength;
        maxDistance = Mathf.Max(0f, maxDistance);

        Vector3 position = ray.origin;
        Vector3Int cell = new Vector3Int(
            Mathf.FloorToInt(position.x),
            Mathf.FloorToInt(position.y),
            Mathf.FloorToInt(position.z));

        Vector3Int step = new Vector3Int(
            direction.x > 0f ? 1 : (direction.x < 0f ? -1 : 0),
            direction.y > 0f ? 1 : (direction.y < 0f ? -1 : 0),
            direction.z > 0f ? 1 : (direction.z < 0f ? -1 : 0));

        float tMaxX = ComputeRayAxisStart(position.x, direction.x, cell.x, step.x);
        float tMaxY = ComputeRayAxisStart(position.y, direction.y, cell.y, step.y);
        float tMaxZ = ComputeRayAxisStart(position.z, direction.z, cell.z, step.z);
        float tDeltaX = ComputeRayAxisDelta(direction.x);
        float tDeltaY = ComputeRayAxisDelta(direction.y);
        float tDeltaZ = ComputeRayAxisDelta(direction.z);

        float distance = 0f;
        Vector3Int previousCell = default;
        bool hasPreviousCell = false;

        while (distance <= maxDistance)
        {
            if (TryGetWorldCellData(cell.x, cell.y, cell.z, out byte materialId, out byte amount) &&
                materialId != 0 &&
                amount > 0)
            {
                hit = new CellRaycastHit
                {
                    Cell = cell,
                    PreviousCell = previousCell,
                    HasPreviousCell = hasPreviousCell,
                    MaterialId = materialId,
                    Amount = amount,
                    Distance = distance
                };
                return true;
            }

            previousCell = cell;
            hasPreviousCell = true;

            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                cell.x += step.x;
                distance = tMaxX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxX && tMaxY <= tMaxZ)
            {
                cell.y += step.y;
                distance = tMaxY;
                tMaxY += tDeltaY;
            }
            else
            {
                cell.z += step.z;
                distance = tMaxZ;
                tMaxZ += tDeltaZ;
            }
        }

        return false;
    }

    public bool TryGetStreamingCenter(out ChunkCoord center)
    {
        center = _streamingCenter;
        return _hasStreamingCenter;
    }

    public void CycleTerrainWireframeMode()
    {
        SetTerrainWireframeMode((TerrainWireframeMode)(((int)_terrainWireframeMode + 1) % 3));
    }

    [ContextMenu("Rebuild All Loaded Chunks")]
    public void RebuildAllLoadedChunks()
    {
        RebuildAllLoadedChunks(true);
    }

    public void RebuildAllLoadedChunks(bool applyCollider)
    {
        foreach (KeyValuePair<ChunkCoord, ChunkData> pair in _chunkStore.Enumerate())
        {
            ChunkCoord coord = pair.Key;
            ChunkData chunk = pair.Value;

            if (!_chunkViews.TryGetValue(coord, out ChunkView view))
            {
                continue;
            }

            chunk.MarkAllSubChunksDirty();
            RebuildDirtySubChunks(chunk, view, applyCollider);
        }
    }

    public void ApplyOrientedBrush(Vector3 worldPosition, Vector3 surfaceNormal, float radius, int deltaAmount)
    {
        ApplyOrientedBrush(worldPosition, surfaceNormal, radius, deltaAmount, 0);
    }

    public void ApplyOrientedBrush(Vector3 worldPosition, Vector3 surfaceNormal, float radius, int deltaAmount, byte paintMaterialId)
    {
        if (!HasGeneratedWorld || radius <= 0f || deltaAmount == 0)
        {
            return;
        }

        int radiusInt = Mathf.CeilToInt(radius);
        int minSampleX = Mathf.RoundToInt(worldPosition.x) - radiusInt;
        int maxSampleX = Mathf.RoundToInt(worldPosition.x) + radiusInt;
        int minSampleY = Mathf.Max(0, Mathf.RoundToInt(worldPosition.y) - radiusInt);
        int maxSampleY = Mathf.Min(WorldConstants.SampleSizeY - 1, Mathf.RoundToInt(worldPosition.y) + radiusInt);
        int minSampleZ = Mathf.RoundToInt(worldPosition.z) - radiusInt;
        int maxSampleZ = Mathf.RoundToInt(worldPosition.z) + radiusInt;

        ModifyDensityInBounds(
            minSampleX,
            maxSampleX,
            minSampleY,
            maxSampleY,
            minSampleZ,
            maxSampleZ,
            worldPosition,
            surfaceNormal,
            radius,
            deltaAmount,
            paintMaterialId);
    }

    public void ApplySphereBrush(Vector3 worldPosition, float radius, int deltaAmount)
    {
        ApplyOrientedBrush(worldPosition, Vector3.up, radius, deltaAmount, 0);
    }

    public void ApplyCellEdit(Vector3Int cell, int deltaAmount, byte paintMaterialId)
    {
        if (!HasGeneratedWorld || deltaAmount == 0)
        {
            return;
        }

        _modifiedChunks.Clear();
        ModifyCell(cell.x, cell.y, cell.z, deltaAmount, paintMaterialId);

        foreach (ChunkCoord coord in _modifiedChunks)
        {
            if (_chunkStore.TryGet(coord, out ChunkData dirtyChunk) &&
                _chunkViews.TryGetValue(coord, out ChunkView dirtyView))
            {
                RebuildDirtySubChunks(dirtyChunk, dirtyView, _applyMeshCollider);
            }
        }
    }

    public void ApplyCellBrush(Vector3Int centerCell, float radius, int deltaAmount, byte paintMaterialId)
    {
        if (!HasGeneratedWorld || radius <= 0f || deltaAmount == 0)
        {
            return;
        }

        int radiusInt = Mathf.CeilToInt(radius);
        float radiusSq = radius * radius;
        _modifiedChunks.Clear();

        for (int worldZ = centerCell.z - radiusInt; worldZ <= centerCell.z + radiusInt; worldZ++)
        {
            for (int worldY = centerCell.y - radiusInt; worldY <= centerCell.y + radiusInt; worldY++)
            {
                for (int worldX = centerCell.x - radiusInt; worldX <= centerCell.x + radiusInt; worldX++)
                {
                    if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
                    {
                        continue;
                    }

                    Vector3 cellCenter = new Vector3(worldX + 0.5f, worldY + 0.5f, worldZ + 0.5f);
                    Vector3 brushCenter = new Vector3(centerCell.x + 0.5f, centerCell.y + 0.5f, centerCell.z + 0.5f);
                    if ((cellCenter - brushCenter).sqrMagnitude > radiusSq)
                    {
                        continue;
                    }

                    ModifyCell(worldX, worldY, worldZ, deltaAmount, paintMaterialId);
                }
            }
        }

        foreach (ChunkCoord coord in _modifiedChunks)
        {
            if (_chunkStore.TryGet(coord, out ChunkData dirtyChunk) &&
                _chunkViews.TryGetValue(coord, out ChunkView dirtyView))
            {
                RebuildDirtySubChunks(dirtyChunk, dirtyView, _applyMeshCollider);
            }
        }
    }

    private void ModifyCell(int worldX, int worldY, int worldZ, int deltaAmount, byte paintMaterialId)
    {
        ChunkCoord coord = WorldMath.WorldCellToChunkCoord(worldX, worldZ);
        if (!_chunkStore.TryGet(coord, out ChunkData chunk))
        {
            return;
        }

        Vector3Int localCell = WorldMath.WorldCellToLocalCell(worldX, worldY, worldZ);
        if (localCell.x < 0 || localCell.x >= WorldConstants.ChunkSizeX ||
            localCell.y < 0 || localCell.y >= WorldConstants.ChunkSizeY ||
            localCell.z < 0 || localCell.z >= WorldConstants.ChunkSizeZ)
        {
            return;
        }

        byte currentId = chunk.GetMaterialId(localCell.x, localCell.y, localCell.z);
        byte currentAmount = chunk.GetMaterialAmount(localCell.x, localCell.y, localCell.z);
        byte nextId = currentId;
        byte nextAmount = currentAmount;

        if (deltaAmount < 0)
        {
            if (currentAmount == 0)
            {
                return;
            }

            nextAmount = (byte)Mathf.Max(0, currentAmount + deltaAmount);
            if (nextAmount == 0)
            {
                nextId = TerrainDensityUtility.AirMaterialId;
            }
        }
        else
        {
            if (paintMaterialId == TerrainDensityUtility.AirMaterialId)
            {
                return;
            }

            if (currentAmount != 0 && currentId != paintMaterialId)
            {
                return;
            }

            nextAmount = (byte)Mathf.Min(255, currentAmount + deltaAmount);
            nextId = nextAmount == 0 ? TerrainDensityUtility.AirMaterialId : paintMaterialId;
        }

        if (nextId == currentId && nextAmount == currentAmount)
        {
            return;
        }

        chunk.SetMaterialId(localCell.x, localCell.y, localCell.z, nextId);
        chunk.SetMaterialAmount(localCell.x, localCell.y, localCell.z, nextAmount);
        _modifiedChunks.Add(coord);
        RefreshDensitiesAroundWorldCell(worldX, worldY, worldZ);
    }

    private void RefreshDensitiesAroundWorldCell(int worldCellX, int worldCellY, int worldCellZ)
    {
        for (int sampleZ = worldCellZ; sampleZ <= worldCellZ + 1; sampleZ++)
        {
            for (int sampleY = worldCellY; sampleY <= worldCellY + 1; sampleY++)
            {
                for (int sampleX = worldCellX; sampleX <= worldCellX + 1; sampleX++)
                {
                    RefreshWorldSampleDensity(sampleX, sampleY, sampleZ);
                }
            }
        }
    }

    private void RefreshWorldSampleDensity(int worldSampleX, int sampleY, int worldSampleZ)
    {
        if (sampleY < 0 || sampleY >= WorldConstants.SampleSizeY)
        {
            return;
        }

        ChunkCoord coord = WorldMath.WorldSampleToChunkCoord(worldSampleX, worldSampleZ);
        if (!_chunkStore.TryGet(coord, out ChunkData chunk))
        {
            return;
        }

        Vector2Int localSample = WorldMath.WorldSampleToLocalSampleXZ(worldSampleX, worldSampleZ);
        sbyte newDensity = ComputeWorldSampleDensityFromCells(worldSampleX, sampleY, worldSampleZ);
        if (chunk.GetDensity(localSample.x, sampleY, localSample.y) == newDensity)
        {
            return;
        }

        chunk.SetDensity(localSample.x, sampleY, localSample.y, newDensity);
        MarkSampleAffectedSubChunks(chunk, sampleY);
        _modifiedChunks.Add(coord);
    }

    private sbyte ComputeWorldSampleDensityFromCells(int worldSampleX, int sampleY, int worldSampleZ)
    {
        int totalDensity = 0;

        for (int offsetZ = 0; offsetZ <= 1; offsetZ++)
        {
            for (int offsetY = 0; offsetY <= 1; offsetY++)
            {
                for (int offsetX = 0; offsetX <= 1; offsetX++)
                {
                    totalDensity += TerrainDensityUtility.CellAmountToMeshingDensity(GetWorldCellAmountOrGenerated(
                        worldSampleX - 1 + offsetX,
                        sampleY - 1 + offsetY,
                        worldSampleZ - 1 + offsetZ));
                }
            }
        }

        int averageDensity = Mathf.RoundToInt(totalDensity / 8f);
        return (sbyte)Mathf.Clamp(averageDensity, WorldConstants.EmptyDensity, WorldConstants.FullDensity);
    }

    private byte GetWorldCellAmountOrGenerated(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
        {
            return 0;
        }

        if (TryGetWorldCellData(worldX, worldY, worldZ, out _, out byte amount))
        {
            return amount;
        }

        return TerrainDensityUtility.SampleCellAmount(_generationSettings, worldX, worldY, worldZ);
    }

    private static float ComputeRayAxisStart(float origin, float direction, int cell, int step)
    {
        if (step == 0 || Mathf.Abs(direction) <= 0.000001f)
        {
            return float.PositiveInfinity;
        }

        float boundary = step > 0 ? cell + 1f : cell;
        return (boundary - origin) / direction;
    }

    private static float ComputeRayAxisDelta(float direction)
    {
        return Mathf.Abs(direction) <= 0.000001f ? float.PositiveInfinity : Mathf.Abs(1f / direction);
    }

    private void Update()
    {
        if (!HasGeneratedWorld)
        {
            return;
        }

        RefreshStreamingCenter(false);
        ProcessCompletedGenerationJobs();
        ProcessCompletedChunkMeshBuilds();
        ProcessCompletedProxyMeshBuilds();
        AdvanceInitialStreamingPhase();
        ProcessQueuedOperations();
        ProcessCompletedGenerationJobs();
        ProcessCompletedChunkMeshBuilds();
        ProcessCompletedProxyMeshBuilds();
        AdvanceInitialStreamingPhase();
    }

    private void LateUpdate()
    {
        if (!HasGeneratedWorld)
        {
            return;
        }
    }

    private void OnDestroy()
    {
        ReleaseTerrainWireframeResources();
        CompleteAndDisposePendingJobs();
        _chunkStore.Dispose();
    }

    private void RefreshStreamingCenter(bool force)
    {
        ChunkCoord center = ResolveStreamingCenterCoord();
        if (!force && _hasStreamingCenter && center == _streamingCenter)
        {
            return;
        }

        _streamingCenter = center;
        _hasStreamingCenter = true;
        RefreshDesiredChunkSet(center);
    }

    private ChunkCoord ResolveStreamingCenterCoord()
    {
        Transform target = _streamingTarget;
        if (target == null && Camera.main != null)
        {
            target = Camera.main.transform;
        }

        if (target == null)
        {
            if (_hasStreamingCenter)
            {
                return _streamingCenter;
            }

            return new ChunkCoord(0, 0);
        }

        int worldX = Mathf.FloorToInt(target.position.x);
        int worldZ = Mathf.FloorToInt(target.position.z);
        return WorldMath.WorldCellToChunkCoord(worldX, worldZ);
    }

    private void RefreshDesiredChunkSet(ChunkCoord center)
    {
        _desiredChunkCoords.Clear();
        _desiredChunkLods.Clear();
        _desiredProxyCoords.Clear();

        if (_finiteMap)
        {
            GetFiniteChunkRect(out int finiteMinX, out int finiteMaxX, out int finiteMinZ, out int finiteMaxZ);

            for (int z = finiteMinZ; z < finiteMaxZ; z++)
            {
                for (int x = finiteMinX; x < finiteMaxX; x++)
                {
                    ChunkCoord coord = new ChunkCoord(x, z);
                    _desiredChunkCoords.Add(coord);
                    _desiredChunkLods[coord] = LOD0;
                }
            }
        }
        else
        {
            GetBaseChunkRect(center, out int baseMinX, out int baseMaxX, out int baseMinZ, out int baseMaxZ);
            int proxy1MinX = baseMinX - Proxy1Border;
            int proxy1MaxX = baseMaxX + Proxy1Border;
            int proxy1MinZ = baseMinZ - Proxy1Border;
            int proxy1MaxZ = baseMaxZ + Proxy1Border;
            int proxy2MinX = proxy1MinX - Proxy2Border;
            int proxy2MaxX = proxy1MaxX + Proxy2Border;
            int proxy2MinZ = proxy1MinZ - Proxy2Border;
            int proxy2MaxZ = proxy1MaxZ + Proxy2Border;

            for (int z = baseMinZ; z < baseMaxZ; z++)
            {
                for (int x = baseMinX; x < baseMaxX; x++)
                {
                    ChunkCoord coord = new ChunkCoord(x, z);
                    _desiredChunkCoords.Add(coord);
                    _desiredChunkLods[coord] = LOD0;
                }
            }

            if (RenderProxyChunks)
            {
                AddDesiredProxies(LOD1, 2, proxy1MinX, proxy1MaxX, proxy1MinZ, proxy1MaxZ, baseMinX, baseMaxX, baseMinZ, baseMaxZ);
                AddDesiredProxies(LOD2, 4, proxy2MinX, proxy2MaxX, proxy2MinZ, proxy2MaxZ, proxy1MinX, proxy1MaxX, proxy1MinZ, proxy1MaxZ);
            }
        }

        _loadBuffer.Clear();
        foreach (KeyValuePair<ChunkCoord, int> pair in _desiredChunkLods)
        {
            ChunkCoord coord = pair.Key;
            int desiredLod = pair.Value;

            if (_activeChunkLods.TryGetValue(coord, out int currentLod) && currentLod == desiredLod)
            {
                continue;
            }

            if (HasPendingJobsForChunk(coord))
            {
                continue;
            }

            _loadBuffer.Add(coord);
        }

        _loadBuffer.Sort((left, right) =>
        {
            int leftDistance = ChunkDistanceSq(left, center);
            int rightDistance = ChunkDistanceSq(right, center);

            if (leftDistance != rightDistance)
            {
                return leftDistance.CompareTo(rightDistance);
            }

            if (left.Z != right.Z)
            {
                return left.Z.CompareTo(right.Z);
            }

            return left.X.CompareTo(right.X);
        });

        for (int i = 0; i < _loadBuffer.Count; i++)
        {
            EnqueueChunkLoad(_loadBuffer[i]);
        }

        _proxyLoadBuffer.Clear();
        foreach (ProxyCoord coord in _desiredProxyCoords)
        {
            if (!IsProxyLoadAllowedInCurrentPhase(coord.LodLevel))
            {
                continue;
            }

            if (_proxyViews.ContainsKey(coord))
            {
                continue;
            }

            if (HasPendingJobsForProxy(coord))
            {
                continue;
            }

            _proxyLoadBuffer.Add(coord);
        }

        _proxyLoadBuffer.Sort((left, right) =>
        {
            int leftDistance = ProxyDistanceSq(left, center);
            int rightDistance = ProxyDistanceSq(right, center);
            if (leftDistance != rightDistance)
            {
                return leftDistance.CompareTo(rightDistance);
            }

            if (left.LodLevel != right.LodLevel)
            {
                return left.LodLevel.CompareTo(right.LodLevel);
            }

            if (left.Z != right.Z)
            {
                return left.Z.CompareTo(right.Z);
            }

            return left.X.CompareTo(right.X);
        });

        for (int i = 0; i < _proxyLoadBuffer.Count; i++)
        {
            EnqueueProxyLoad(_proxyLoadBuffer[i]);
        }

        _coordBuffer.Clear();
        foreach (KeyValuePair<ChunkCoord, ChunkView> pair in _chunkViews)
        {
            _coordBuffer.Add(pair.Key);
        }

        _unloadBuffer.Clear();
        for (int i = 0; i < _coordBuffer.Count; i++)
        {
            ChunkCoord coord = _coordBuffer[i];
            if (_desiredChunkCoords.Contains(coord))
            {
                continue;
            }

            _unloadBuffer.Add(coord);
        }

        _unloadBuffer.Sort((left, right) =>
        {
            int leftDistance = ChunkDistanceSq(left, center);
            int rightDistance = ChunkDistanceSq(right, center);

            if (leftDistance != rightDistance)
            {
                return rightDistance.CompareTo(leftDistance);
            }

            if (left.Z != right.Z)
            {
                return right.Z.CompareTo(left.Z);
            }

            return right.X.CompareTo(left.X);
        });

        for (int i = 0; i < _unloadBuffer.Count; i++)
        {
            EnqueueChunkUnload(_unloadBuffer[i]);
        }

        _proxyCoordBuffer.Clear();
        foreach (KeyValuePair<ProxyCoord, ProxyChunkView> pair in _proxyViews)
        {
            _proxyCoordBuffer.Add(pair.Key);
        }

        _proxyUnloadBuffer.Clear();
        for (int i = 0; i < _proxyCoordBuffer.Count; i++)
        {
            ProxyCoord coord = _proxyCoordBuffer[i];
            if (_desiredProxyCoords.Contains(coord))
            {
                continue;
            }

            _proxyUnloadBuffer.Add(coord);
        }

        _proxyUnloadBuffer.Sort((left, right) =>
        {
            int leftDistance = ProxyDistanceSq(left, center);
            int rightDistance = ProxyDistanceSq(right, center);
            if (leftDistance != rightDistance)
            {
                return rightDistance.CompareTo(leftDistance);
            }

            if (left.LodLevel != right.LodLevel)
            {
                return right.LodLevel.CompareTo(left.LodLevel);
            }

            if (left.Z != right.Z)
            {
                return right.Z.CompareTo(left.Z);
            }

            return right.X.CompareTo(left.X);
        });

        for (int i = 0; i < _proxyUnloadBuffer.Count; i++)
        {
            EnqueueProxyUnload(_proxyUnloadBuffer[i]);
        }
    }

    private void EnqueueChunkLoad(ChunkCoord coord)
    {
        if (_pendingLoadSet.Add(coord))
        {
            _pendingChunkLoads.Enqueue(coord);
        }
    }

    private void EnqueueChunkUnload(ChunkCoord coord)
    {
        if (_pendingUnloadSet.Add(coord))
        {
            _pendingChunkUnloads.Enqueue(coord);
        }
    }

    private void EnqueueProxyLoad(ProxyCoord coord)
    {
        if (_pendingProxyLoadSet.Add(coord))
        {
            _pendingProxyLoads.Enqueue(coord);
        }
    }

    private void EnqueueProxyUnload(ProxyCoord coord)
    {
        if (_pendingProxyUnloadSet.Add(coord))
        {
            _pendingProxyUnloads.Enqueue(coord);
        }
    }

    private void ProcessQueuedOperations()
    {
        int unloadBudget = Mathf.Max(0, _maxChunkUnloadsPerFrame);
        int unloaded = 0;
        _deferredUnloadBuffer.Clear();
        _deferredProxyUnloadBuffer.Clear();

        while (unloaded < unloadBudget && _pendingChunkUnloads.Count > 0)
        {
            ChunkCoord coord = _pendingChunkUnloads.Dequeue();
            _pendingUnloadSet.Remove(coord);

            if (_desiredChunkCoords.Contains(coord))
            {
                continue;
            }

            if (HasPendingJobsForChunk(coord))
            {
                _deferredUnloadBuffer.Add(coord);
                continue;
            }

            UnloadChunk(coord);
            unloaded++;
        }

        while (unloaded < unloadBudget && _pendingProxyUnloads.Count > 0)
        {
            ProxyCoord coord = _pendingProxyUnloads.Dequeue();
            _pendingProxyUnloadSet.Remove(coord);

            if (_desiredProxyCoords.Contains(coord))
            {
                continue;
            }

            if (HasPendingJobsForProxy(coord))
            {
                _deferredProxyUnloadBuffer.Add(coord);
                continue;
            }

            UnloadProxy(coord);
            unloaded++;
        }

        for (int i = 0; i < _deferredUnloadBuffer.Count; i++)
        {
            EnqueueChunkUnload(_deferredUnloadBuffer[i]);
        }

        for (int i = 0; i < _deferredProxyUnloadBuffer.Count; i++)
        {
            EnqueueProxyUnload(_deferredProxyUnloadBuffer[i]);
        }

        int lod0Budget = Mathf.Max(0, _maxLod0LoadsPerFrame);
        while (lod0Budget > 0 && _pendingChunkLoads.Count > 0)
        {
            ChunkCoord coord = _pendingChunkLoads.Dequeue();
            _pendingLoadSet.Remove(coord);

            if (!_desiredChunkLods.TryGetValue(coord, out int desiredLod))
            {
                continue;
            }

            EnsureChunkAtDesiredLod(coord, desiredLod);
            lod0Budget--;
        }

        ProcessProxyLoadQueue(LOD1, Mathf.Max(0, _maxProxy1LoadsPerFrame));
        ProcessProxyLoadQueue(LOD2, Mathf.Max(0, _maxProxy2LoadsPerFrame));
    }

    private void ProcessProxyLoadQueue(int lodLevel, int budget)
    {
        if (budget <= 0 || _pendingProxyLoads.Count == 0)
        {
            return;
        }

        int attempts = _pendingProxyLoads.Count;
        int loaded = 0;
        while (attempts > 0 && loaded < budget && _pendingProxyLoads.Count > 0)
        {
            attempts--;
            ProxyCoord coord = _pendingProxyLoads.Dequeue();
            _pendingProxyLoadSet.Remove(coord);

            if (coord.LodLevel != lodLevel)
            {
                EnqueueProxyLoad(coord);
                continue;
            }

            if (!_desiredProxyCoords.Contains(coord))
            {
                continue;
            }

            EnsureProxyAtDesiredLod(coord);
            loaded++;
        }
    }

    private void GenerateChunk(ChunkCoord coord)
    {
        ChunkData chunk = new ChunkData(coord);
        _chunkStore.Add(chunk);

        TerrainGenerationJob generationJob = new TerrainGenerationJob
        {
            Coord = coord,
            Settings = _generationSettings,
            MaterialIds = chunk.MaterialIds,
            MaterialAmounts = chunk.MaterialAmounts
        };

        TerrainSampleDensityFromCellsJob densityJob = new TerrainSampleDensityFromCellsJob
        {
            Coord = coord,
            Settings = _generationSettings,
            MaterialAmounts = chunk.MaterialAmounts,
            Density = chunk.Density
        };

        JobHandle cellHandle = generationJob.Schedule(WorldConstants.ChunkCellCount, 128);
        JobHandle densityHandle = densityJob.Schedule(WorldConstants.ChunkSampleCount, 128, cellHandle);

        PendingChunkGeneration operation = new PendingChunkGeneration
        {
            Coord = coord,
            Handle = densityHandle
        };

        _pendingGenerations.Add(coord, operation);
    }

    private void EnsureChunkAtDesiredLod(ChunkCoord coord, int desiredLod)
    {
        if (_pendingGenerations.ContainsKey(coord) || _pendingChunkMeshBuilds.ContainsKey(coord))
        {
            return;
        }

        _activeChunkLods.TryGetValue(coord, out int currentLod);

        if (_chunkViews.TryGetValue(coord, out ChunkView existingView) && currentLod == desiredLod)
        {
            return;
        }

        if (desiredLod != LOD0)
        {
            return;
        }

        if (!_chunkStore.Contains(coord))
        {
            GenerateChunk(coord);
            return;
        }

        if (!_chunkStore.TryGet(coord, out ChunkData chunk))
        {
            return;
        }

        Transform root = _chunkRoot != null ? _chunkRoot : transform;
        ChunkView view = _chunkViews.TryGetValue(coord, out ChunkView currentView)
            ? currentView
            : AcquireChunkView(coord, root);

        _chunkViews[coord] = view;
        ScheduleChunkLoadBuild(chunk, view, _applyMeshCollider);
    }

    private void EnsureProxyAtDesiredLod(ProxyCoord coord)
    {
        if (HasPendingJobsForProxy(coord))
        {
            return;
        }

        if (_proxyViews.ContainsKey(coord))
        {
            return;
        }

        Transform root = _chunkRoot != null ? _chunkRoot : transform;
        ProxyChunkView view = AcquireProxyView(coord, root);
        _proxyViews[coord] = view;
        ScheduleProxyChunkBuild(coord, view);
    }

    private void ProcessCompletedGenerationJobs()
    {
        if (_pendingGenerations.Count == 0)
        {
            return;
        }

        _completedGenerationBuffer.Clear();
        foreach (KeyValuePair<ChunkCoord, PendingChunkGeneration> pair in _pendingGenerations)
        {
            if (pair.Value.Handle.IsCompleted)
            {
                _completedGenerationBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < _completedGenerationBuffer.Count; i++)
        {
            ChunkCoord coord = _completedGenerationBuffer[i];
            PendingChunkGeneration operation = _pendingGenerations[coord];
            operation.Handle.Complete();
            _pendingGenerations.Remove(coord);

            if (!_chunkStore.TryGet(coord, out ChunkData chunk))
            {
                continue;
            }

            if (!_desiredChunkLods.TryGetValue(coord, out int desiredLod))
            {
                _chunkStore.RemoveAndDispose(coord);
                continue;
            }

            if (desiredLod != LOD0)
            {
                if (_activeChunkLods.TryGetValue(coord, out int currentLod) && currentLod == desiredLod)
                {
                    _chunkStore.RemoveAndDispose(coord);
                    continue;
                }

                EnsureChunkAtDesiredLod(coord, desiredLod);
                continue;
            }

            Transform root = _chunkRoot != null ? _chunkRoot : transform;
            ChunkView view = _chunkViews.TryGetValue(coord, out ChunkView currentView)
                ? currentView
                : AcquireChunkView(coord, root);
            _chunkViews[coord] = view;
            ScheduleChunkLoadBuild(chunk, view, _applyMeshCollider);
        }
    }

    private void ProcessCompletedChunkMeshBuilds()
    {
        if (_pendingChunkMeshBuilds.Count == 0)
        {
            return;
        }

        _completedChunkMeshBuffer.Clear();
        int applyBudget = Mathf.Max(0, _maxLod0AppliesPerFrame);
        foreach (KeyValuePair<ChunkCoord, PendingChunkMeshBuild> pair in _pendingChunkMeshBuilds)
        {
            if (applyBudget <= 0)
            {
                break;
            }

            if (TryAdvanceChunkMeshBuild(pair.Value))
            {
                _completedChunkMeshBuffer.Add(pair.Key);
                applyBudget--;
            }
        }

        for (int i = 0; i < _completedChunkMeshBuffer.Count; i++)
        {
            ChunkCoord coord = _completedChunkMeshBuffer[i];
            PendingChunkMeshBuild build = _pendingChunkMeshBuilds[coord];
            _pendingChunkMeshBuilds.Remove(coord);
            FinalizeChunkMeshBuild(build);
        }
    }

    private void ProcessCompletedProxyMeshBuilds()
    {
        if (_pendingProxyMeshBuilds.Count == 0)
        {
            return;
        }

        _completedProxyMeshBuffer.Clear();
        int proxy1Budget = Mathf.Max(0, _maxProxy1AppliesPerFrame);
        int proxy2Budget = Mathf.Max(0, _maxProxy2AppliesPerFrame);
        foreach (KeyValuePair<ProxyCoord, PendingProxyMeshBuild> pair in _pendingProxyMeshBuilds)
        {
            PendingProxyMeshBuild build = pair.Value;
            int budget = build.Coord.LodLevel == LOD1 ? proxy1Budget : proxy2Budget;
            if (budget <= 0)
            {
                continue;
            }

            if (TryAdvanceProxyMeshBuild(build))
            {
                _completedProxyMeshBuffer.Add(pair.Key);
                if (build.Coord.LodLevel == LOD1)
                {
                    proxy1Budget--;
                }
                else
                {
                    proxy2Budget--;
                }
            }
        }

        for (int i = 0; i < _completedProxyMeshBuffer.Count; i++)
        {
            ProxyCoord coord = _completedProxyMeshBuffer[i];
            PendingProxyMeshBuild build = _pendingProxyMeshBuilds[coord];
            _pendingProxyMeshBuilds.Remove(coord);
            FinalizeProxyMeshBuild(build);
        }
    }

    private void UnloadChunk(ChunkCoord coord)
    {
        _chunkStore.RemoveAndDispose(coord);
        _activeChunkLods.Remove(coord);

        if (_chunkViews.TryGetValue(coord, out ChunkView view))
        {
            _chunkViews.Remove(coord);
            ReleaseChunkView(view);
        }
    }

    private void UnloadProxy(ProxyCoord coord)
    {
        if (_proxyViews.TryGetValue(coord, out ProxyChunkView view))
        {
            _proxyViews.Remove(coord);
            ReleaseProxyView(view);
        }
    }

    private ChunkView AcquireChunkView(ChunkCoord coord, Transform root)
    {
        EnsureTerrainMaterialConfigured();

        ChunkView view;
        if (_chunkViewPool.Count > 0)
        {
            view = _chunkViewPool.Pop();
            view.transform.SetParent(root, false);
            view.gameObject.SetActive(true);
        }
        else if (_chunkViewPrefab != null)
        {
            view = Instantiate(_chunkViewPrefab, root);
        }
        else
        {
            GameObject go = new GameObject($"Chunk_{coord.X}_{coord.Z}");
            go.transform.SetParent(root, false);
            view = go.AddComponent<ChunkView>();
        }

        view.SetChunkCoord(coord);
        view.SetMaterial(_terrainMaterial);
        ApplyTerrainWireframeState(view);
        return view;
    }

    private ProxyChunkView AcquireProxyView(ProxyCoord coord, Transform root)
    {
        EnsureTerrainMaterialConfigured();

        ProxyChunkView view;
        if (_proxyViewPool.Count > 0)
        {
            view = _proxyViewPool.Pop();
            view.transform.SetParent(root, false);
            view.gameObject.SetActive(true);
        }
        else
        {
            GameObject go = new GameObject($"Proxy_L{coord.LodLevel}_{coord.X}_{coord.Z}");
            go.transform.SetParent(root, false);
            view = go.AddComponent<ProxyChunkView>();
        }

        view.SetProxyCoord(coord);
        view.SetMaterial(_terrainMaterial);
        ApplyTerrainWireframeState(view);
        return view;
    }

    private void ReleaseChunkView(ChunkView view)
    {
        if (view == null)
        {
            return;
        }

        view.ClearAllSubChunks();
        view.gameObject.SetActive(false);

        if (_chunkViewPool.Count >= MaxChunkViewPoolCount)
        {
            DestroyChunkView(view);
            return;
        }

        view.transform.SetParent(_chunkRoot != null ? _chunkRoot : transform, false);
        _chunkViewPool.Push(view);
    }

    private void ReleaseProxyView(ProxyChunkView view)
    {
        if (view == null)
        {
            return;
        }

        view.ClearMesh();
        view.gameObject.SetActive(false);

        if (_proxyViewPool.Count >= MaxChunkViewPoolCount)
        {
            DestroyProxyView(view);
            return;
        }

        view.transform.SetParent(_chunkRoot != null ? _chunkRoot : transform, false);
        _proxyViewPool.Push(view);
    }

    private void DestroyChunkView(ChunkView view)
    {
        if (view == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(view.gameObject);
        }
        else
        {
            DestroyImmediate(view.gameObject);
        }
    }

    private void DestroyProxyView(ProxyChunkView view)
    {
        if (view == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(view.gameObject);
        }
        else
        {
            DestroyImmediate(view.gameObject);
        }
    }

    private void SetTerrainWireframeMode(TerrainWireframeMode mode)
    {
        _terrainWireframeMode = mode;
        ApplyTerrainWireframeStateToLoadedViews();
    }

    private void ApplyTerrainWireframeStateToLoadedViews()
    {
        foreach (ChunkView view in _chunkViews.Values)
        {
            ApplyTerrainWireframeState(view);
        }

        foreach (ProxyChunkView view in _proxyViews.Values)
        {
            ApplyTerrainWireframeState(view);
        }
    }

    private void ApplyTerrainWireframeState(ChunkView view)
    {
        if (view == null)
        {
            return;
        }

        bool wireVisible = _terrainWireframeMode != TerrainWireframeMode.Off;
        bool solidVisible = _terrainWireframeMode != TerrainWireframeMode.WireOnly;
        Material wireMaterial = wireVisible ? EnsureTerrainWireframeMaterial() : null;
        view.SetTerrainWireframeState(solidVisible, wireVisible, wireMaterial);
    }

    private void ApplyTerrainWireframeState(ProxyChunkView view)
    {
        if (view == null)
        {
            return;
        }

        bool wireVisible = _terrainWireframeMode != TerrainWireframeMode.Off;
        bool solidVisible = _terrainWireframeMode != TerrainWireframeMode.WireOnly;
        Material wireMaterial = wireVisible ? EnsureTerrainWireframeMaterial() : null;
        view.SetWireframeState(solidVisible, wireVisible, wireMaterial);
    }

    private Material EnsureTerrainWireframeMaterial()
    {
        if (_terrainWireframeMaterial != null)
        {
            return _terrainWireframeMaterial;
        }

        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color") ??
            Shader.Find("Sprites/Default");

        if (shader == null)
        {
            return null;
        }

        _terrainWireframeMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10
        };

        _terrainWireframeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _terrainWireframeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _terrainWireframeMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _terrainWireframeMaterial.SetInt("_ZWrite", 0);

        if (_terrainWireframeMaterial.HasProperty("_BaseColor"))
        {
            _terrainWireframeMaterial.SetColor("_BaseColor", _terrainWireframeColor);
        }

        if (_terrainWireframeMaterial.HasProperty("_Color"))
        {
            _terrainWireframeMaterial.SetColor("_Color", _terrainWireframeColor);
        }

        return _terrainWireframeMaterial;
    }

    private void ReleaseTerrainWireframeResources()
    {
        if (_terrainWireframeMaterial == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(_terrainWireframeMaterial);
        }
        else
        {
            DestroyImmediate(_terrainWireframeMaterial);
        }

        _terrainWireframeMaterial = null;
    }

    private static string GetTerrainWireframeModeLabel(TerrainWireframeMode mode)
    {
        switch (mode)
        {
            case TerrainWireframeMode.Overlay:
                return "Overlay";
            case TerrainWireframeMode.WireOnly:
                return "WireOnly";
            default:
                return "Off";
        }
    }

    private void EnsureTerrainMaterialConfigured()
    {
        if (_terrainMaterial == null || _terrainMaterialLibrary == null)
        {
            return;
        }

        if (!_terrainMaterialLibrary.TryApplyToMaterial(_terrainMaterial, out string error))
        {
            Debug.LogError($"Failed to apply terrain material library: {error}", this);
        }
    }

    private void ScheduleChunkLoadBuild(ChunkData chunk, ChunkView view, bool applyCollider)
    {
        chunk.MarkAllSubChunksDirty();
        bool hideUntilReady = !_activeChunkLods.ContainsKey(chunk.Coord);

        PendingChunkMeshBuild build = new PendingChunkMeshBuild
        {
            Coord = chunk.Coord,
            TargetLod = LOD0,
            View = view,
            ApplyCollider = applyCollider,
            HideUntilReady = hideUntilReady,
            SubChunkBuilds = new PendingSubChunkBuild[WorldConstants.SubChunkCount]
        };

        if (build.HideUntilReady)
        {
            view.ClearAllSubChunks();
            view.gameObject.SetActive(false);
        }

        for (int subChunkIndex = 0; subChunkIndex < WorldConstants.SubChunkCount; subChunkIndex++)
        {
            SubChunkView subChunkView = view.GetSubChunk(subChunkIndex);
            if (subChunkView == null)
            {
                continue;
            }

            PendingSubChunkBuild operation = new PendingSubChunkBuild
            {
                SubChunkIndex = subChunkIndex,
                CellCount = WorldConstants.SubChunkCellCount,
                View = subChunkView,
                TriangleCounts = new NativeArray<byte>(WorldConstants.SubChunkCellCount, Allocator.Persistent),
                TriangleOffsets = new NativeArray<int>(WorldConstants.SubChunkCellCount, Allocator.Persistent)
            };

            SubChunkTriangleCountJob countJob = new SubChunkTriangleCountJob
            {
                Density = chunk.Density,
                SubChunkIndex = subChunkIndex,
                TriangleCounts = operation.TriangleCounts
            };

            operation.Handle = countJob.Schedule(WorldConstants.SubChunkCellCount, 128);
            build.SubChunkBuilds[subChunkIndex] = operation;
        }

        _pendingChunkMeshBuilds[chunk.Coord] = build;
    }

    private void ScheduleLodChunkBuild(ChunkCoord coord, ChunkView view, int lodLevel, bool applyCollider, bool hideUntilReady)
    {
        int horizontalStep = GetHorizontalStepForLod(lodLevel);
        int cellsX = WorldConstants.ChunkSizeX / horizontalStep;
        int cellsZ = WorldConstants.ChunkSizeZ / horizontalStep;
        int subChunkCellCount = cellsX * WorldConstants.SubChunkSize * cellsZ;

        PendingChunkMeshBuild build = new PendingChunkMeshBuild
        {
            Coord = coord,
            TargetLod = lodLevel,
            View = view,
            ApplyCollider = applyCollider,
            HideUntilReady = hideUntilReady,
            SubChunkBuilds = new PendingSubChunkBuild[WorldConstants.SubChunkCount]
        };

        if (hideUntilReady)
        {
            view.ClearAllSubChunks();
            view.gameObject.SetActive(false);
        }

        for (int subChunkIndex = 0; subChunkIndex < WorldConstants.SubChunkCount; subChunkIndex++)
        {
            SubChunkView subChunkView = view.GetSubChunk(subChunkIndex);
            if (subChunkView == null)
            {
                continue;
            }

            PendingSubChunkBuild operation = new PendingSubChunkBuild
            {
                SubChunkIndex = subChunkIndex,
                CellCount = subChunkCellCount,
                View = subChunkView,
                TriangleCounts = new NativeArray<byte>(subChunkCellCount, Allocator.Persistent),
                TriangleOffsets = new NativeArray<int>(subChunkCellCount, Allocator.Persistent)
            };

            LodSubChunkTriangleCountJob countJob = new LodSubChunkTriangleCountJob
            {
                Coord = coord,
                Settings = _generationSettings,
                SubChunkIndex = subChunkIndex,
                HorizontalStep = horizontalStep,
                CellsX = cellsX,
                CellsZ = cellsZ,
                TriangleCounts = operation.TriangleCounts
            };

            operation.Handle = countJob.Schedule(subChunkCellCount, 128);
            build.SubChunkBuilds[subChunkIndex] = operation;
        }

        _pendingChunkMeshBuilds[coord] = build;
    }

    private bool TryAdvanceChunkMeshBuild(PendingChunkMeshBuild build)
    {
        bool allReady = true;
        int horizontalStep = GetHorizontalStepForLod(build.TargetLod);
        int cellsX = WorldConstants.ChunkSizeX / horizontalStep;
        int cellsZ = WorldConstants.ChunkSizeZ / horizontalStep;

        for (int i = 0; i < build.SubChunkBuilds.Length; i++)
        {
            PendingSubChunkBuild operation = build.SubChunkBuilds[i];
            if (operation == null || operation.ReadyToApply)
            {
                continue;
            }

            if (!operation.Handle.IsCompleted)
            {
                allReady = false;
                continue;
            }

            operation.Handle.Complete();

            if (!operation.WriteScheduled)
            {
                int totalTriangles = SubChunkTrianglePrefixSum.Build(operation.TriangleCounts, operation.TriangleOffsets);
                if (totalTriangles == 0)
                {
                    operation.ReadyToApply = true;
                    continue;
                }

                if (!_chunkStore.TryGet(build.Coord, out ChunkData chunk))
                {
                    if (build.TargetLod == LOD0)
                    {
                        operation.ReadyToApply = true;
                        continue;
                    }
                }

                operation.MeshData = new SubChunkMeshData(totalTriangles, Allocator.Persistent);

                if (build.TargetLod == LOD0)
                {
                    SubChunkMeshWriteJob writeJob = new SubChunkMeshWriteJob
                    {
                        Density = chunk.Density,
                        MaterialIds = chunk.MaterialIds,
                        TriangleCounts = operation.TriangleCounts,
                        TriangleOffsets = operation.TriangleOffsets,
                        SubChunkIndex = operation.SubChunkIndex,
                        BlendDeadZoneMin = BlendDeadZoneMin,
                        BlendDeadZoneMax = BlendDeadZoneMax,
                        Vertices = operation.MeshData.Vertices,
                        Normals = operation.MeshData.Normals,
                        Indices = operation.MeshData.Indices,
                        MaterialInfo = operation.MeshData.MaterialInfo
                    };

                    operation.Handle = writeJob.Schedule(operation.CellCount, 128);
                }
                else
                {
                    LodSubChunkMeshWriteJob writeJob = new LodSubChunkMeshWriteJob
                    {
                        Coord = build.Coord,
                        Settings = _generationSettings,
                        SubChunkIndex = operation.SubChunkIndex,
                        HorizontalStep = horizontalStep,
                        CellsX = cellsX,
                        CellsZ = cellsZ,
                        BlendDeadZoneMin = BlendDeadZoneMin,
                        BlendDeadZoneMax = BlendDeadZoneMax,
                        TriangleCounts = operation.TriangleCounts,
                        TriangleOffsets = operation.TriangleOffsets,
                        Vertices = operation.MeshData.Vertices,
                        Normals = operation.MeshData.Normals,
                        Indices = operation.MeshData.Indices,
                        MaterialInfo = operation.MeshData.MaterialInfo
                    };

                    operation.Handle = writeJob.Schedule(operation.CellCount, 128);
                }

                operation.WriteScheduled = true;
                allReady = false;
                continue;
            }

            operation.ReadyToApply = true;
        }

        return allReady;
    }

    private void FinalizeChunkMeshBuild(PendingChunkMeshBuild build)
    {
        try
        {
            bool isDesired = _desiredChunkLods.TryGetValue(build.Coord, out int desiredLod);
            bool hasChunk = _chunkStore.TryGet(build.Coord, out ChunkData chunk);
            bool hasView = _chunkViews.TryGetValue(build.Coord, out ChunkView currentView);
            bool shouldApply =
                isDesired &&
                desiredLod == build.TargetLod &&
                hasView &&
                currentView == build.View &&
                (build.TargetLod != LOD0 || hasChunk);

            if (!shouldApply)
            {
                if (hasView && currentView == build.View)
                {
                    if (!isDesired)
                    {
                        _chunkViews.Remove(build.Coord);
                        _activeChunkLods.Remove(build.Coord);
                        ReleaseChunkView(currentView);
                    }
                }

                if (!isDesired && hasChunk)
                {
                    _chunkStore.RemoveAndDispose(build.Coord);
                }

                if (isDesired && desiredLod != build.TargetLod)
                {
                    EnqueueChunkLoad(build.Coord);
                }

                return;
            }

            for (int i = 0; i < build.SubChunkBuilds.Length; i++)
            {
                PendingSubChunkBuild operation = build.SubChunkBuilds[i];
                if (operation == null || operation.View == null)
                {
                    continue;
                }

                if (operation.MeshData != null &&
                    operation.MeshData.IsCreated &&
                    operation.MeshData.Vertices.Length > 0 &&
                    operation.MeshData.Indices.Length > 0)
                {
                    MeshApplyUtility.ApplyToSubChunk(operation.View, operation.MeshData, build.ApplyCollider);
                }
                else
                {
                    operation.View.ClearMesh();
                }

                if (build.TargetLod == LOD0)
                {
                    chunk.ClearSubChunkDirty(operation.SubChunkIndex);
                }
            }

            _activeChunkLods[build.Coord] = build.TargetLod;
            build.View.gameObject.SetActive(true);

            if (build.TargetLod != LOD0)
            {
                _chunkStore.RemoveAndDispose(build.Coord);
            }
        }
        finally
        {
            build.Dispose();
        }
    }

    private void ScheduleProxyChunkBuild(ProxyCoord coord, ProxyChunkView view)
    {
        int horizontalStep = GetHorizontalStepForLod(coord.LodLevel);
        int regionChunkSpan = GetProxyChunkSpanForLod(coord.LodLevel);
        int cellsX = (WorldConstants.ChunkSizeX * regionChunkSpan) / horizontalStep;
        int cellsZ = (WorldConstants.ChunkSizeZ * regionChunkSpan) / horizontalStep;
        int cellCount = cellsX * WorldConstants.ChunkSizeY * cellsZ;

        PendingProxyMeshBuild build = new PendingProxyMeshBuild
        {
            Coord = coord,
            View = view,
            TriangleCounts = new NativeArray<byte>(cellCount, Allocator.Persistent),
            TriangleOffsets = new NativeArray<int>(cellCount, Allocator.Persistent)
        };

        LodProxyTriangleCountJob countJob = new LodProxyTriangleCountJob
        {
            OriginChunkX = coord.X,
            OriginChunkZ = coord.Z,
            Settings = _generationSettings,
            HorizontalStep = horizontalStep,
            RegionChunkSpan = regionChunkSpan,
            CellsX = cellsX,
            CellsZ = cellsZ,
            TriangleCounts = build.TriangleCounts
        };

        build.Handle = countJob.Schedule(cellCount, 128);
        _pendingProxyMeshBuilds[coord] = build;
        view.gameObject.SetActive(false);
    }

    private bool TryAdvanceProxyMeshBuild(PendingProxyMeshBuild build)
    {
        if (!build.Handle.IsCompleted)
        {
            return false;
        }

        build.Handle.Complete();

        int horizontalStep = GetHorizontalStepForLod(build.Coord.LodLevel);
        int regionChunkSpan = GetProxyChunkSpanForLod(build.Coord.LodLevel);
        int cellsX = (WorldConstants.ChunkSizeX * regionChunkSpan) / horizontalStep;
        int cellsZ = (WorldConstants.ChunkSizeZ * regionChunkSpan) / horizontalStep;
        int cellCount = cellsX * WorldConstants.ChunkSizeY * cellsZ;

        if (!build.WriteScheduled)
        {
            int totalTriangles = SubChunkTrianglePrefixSum.Build(build.TriangleCounts, build.TriangleOffsets);
            if (totalTriangles == 0)
            {
                return true;
            }

            build.MeshData = new SubChunkMeshData(totalTriangles, Allocator.Persistent);

            LodProxyMeshWriteJob writeJob = new LodProxyMeshWriteJob
            {
                OriginChunkX = build.Coord.X,
                OriginChunkZ = build.Coord.Z,
                Settings = _generationSettings,
                HorizontalStep = horizontalStep,
                RegionChunkSpan = regionChunkSpan,
                CellsX = cellsX,
                CellsZ = cellsZ,
                BlendDeadZoneMin = BlendDeadZoneMin,
                BlendDeadZoneMax = BlendDeadZoneMax,
                TriangleCounts = build.TriangleCounts,
                TriangleOffsets = build.TriangleOffsets,
                Vertices = build.MeshData.Vertices,
                Normals = build.MeshData.Normals,
                Indices = build.MeshData.Indices,
                MaterialInfo = build.MeshData.MaterialInfo
            };

            build.Handle = writeJob.Schedule(cellCount, 128);
            build.WriteScheduled = true;
            return false;
        }

        return true;
    }

    private void FinalizeProxyMeshBuild(PendingProxyMeshBuild build)
    {
        try
        {
            bool isDesired = _desiredProxyCoords.Contains(build.Coord);
            bool hasView = _proxyViews.TryGetValue(build.Coord, out ProxyChunkView currentView);
            bool shouldApply = isDesired && hasView && currentView == build.View;

            if (!shouldApply)
            {
                if (hasView && currentView == build.View && !isDesired)
                {
                    _proxyViews.Remove(build.Coord);
                    ReleaseProxyView(currentView);
                }

                if (isDesired && (!hasView || currentView != build.View))
                {
                    EnqueueProxyLoad(build.Coord);
                }

                return;
            }

            if (build.MeshData != null &&
                build.MeshData.IsCreated &&
                build.MeshData.Vertices.Length > 0 &&
                build.MeshData.Indices.Length > 0)
            {
                MeshApplyUtility.ApplyToProxy(build.View, build.MeshData);
            }
            else
            {
                build.View.ClearMesh();
            }

            build.View.gameObject.SetActive(true);
        }
        finally
        {
            build.Dispose();
        }
    }

    private void RebuildDirtySubChunks(ChunkData chunk, ChunkView view, bool applyCollider)
    {
        for (int subChunkIndex = 0; subChunkIndex < WorldConstants.SubChunkCount; subChunkIndex++)
        {
            if (!chunk.IsSubChunkDirty(subChunkIndex))
            {
                continue;
            }

            RebuildSubChunk(chunk, view, subChunkIndex, applyCollider);
            chunk.ClearSubChunkDirty(subChunkIndex);
        }

    }

    private void RebuildSubChunk(ChunkData chunk, ChunkView view, int subChunkIndex, bool applyCollider)
    {
        SubChunkView subChunkView = view.GetSubChunk(subChunkIndex);
        if (subChunkView == null)
        {
            return;
        }

        NativeArray<byte> triangleCounts = new NativeArray<byte>(WorldConstants.SubChunkCellCount, Allocator.TempJob);
        NativeArray<int> triangleOffsets = new NativeArray<int>(WorldConstants.SubChunkCellCount, Allocator.TempJob);

        try
        {
            SubChunkTriangleCountJob countJob = new SubChunkTriangleCountJob
            {
                Density = chunk.Density,
                SubChunkIndex = subChunkIndex,
                TriangleCounts = triangleCounts
            };

            JobHandle countHandle = countJob.Schedule(WorldConstants.SubChunkCellCount, 128);
            countHandle.Complete();

            int totalTriangles = SubChunkTrianglePrefixSum.Build(triangleCounts, triangleOffsets);

            if (totalTriangles == 0)
            {
                subChunkView.ClearMesh();
                return;
            }

            using (SubChunkMeshData meshData = new SubChunkMeshData(totalTriangles, Allocator.TempJob))
            {
                SubChunkMeshWriteJob writeJob = new SubChunkMeshWriteJob
                {
                    Density = chunk.Density,
                    MaterialIds = chunk.MaterialIds,
                    TriangleCounts = triangleCounts,
                    TriangleOffsets = triangleOffsets,
                    SubChunkIndex = subChunkIndex,
                    BlendDeadZoneMin = BlendDeadZoneMin,
                    BlendDeadZoneMax = BlendDeadZoneMax,
                    Vertices = meshData.Vertices,
                    Normals = meshData.Normals,
                    Indices = meshData.Indices,
                    MaterialInfo = meshData.MaterialInfo
                };

                JobHandle writeHandle = writeJob.Schedule(WorldConstants.SubChunkCellCount, 128);
                writeHandle.Complete();

                MeshApplyUtility.ApplyToSubChunk(subChunkView, meshData, applyCollider);
            }
        }
        finally
        {
            if (triangleCounts.IsCreated)
            {
                triangleCounts.Dispose();
            }

            if (triangleOffsets.IsCreated)
            {
                triangleOffsets.Dispose();
            }
        }
    }

    private void ModifyDensityInBounds(
        int minSampleX,
        int maxSampleX,
        int minSampleY,
        int maxSampleY,
        int minSampleZ,
        int maxSampleZ,
        Vector3 brushCenter,
        Vector3 surfaceNormal,
        float radius,
        int deltaAmount,
        byte paintMaterialId)
    {
        _modifiedChunks.Clear();
        _preExistingSolidCells.Clear();

        if (deltaAmount > 0 && paintMaterialId != 0)
        {
            CacheSolidCellsInBounds(
                minSampleX,
                maxSampleX,
                minSampleY,
                maxSampleY,
                minSampleZ,
                maxSampleZ);
        }

        ModifySharedSamplesInBounds(
            minSampleX,
            maxSampleX,
            minSampleY,
            maxSampleY,
            minSampleZ,
            maxSampleZ,
            brushCenter,
            surfaceNormal,
            radius,
            deltaAmount);

        if (deltaAmount > 0 && paintMaterialId != 0)
        {
            PaintMaterialsInBounds(
                minSampleX,
                maxSampleX,
                minSampleY,
                maxSampleY,
                minSampleZ,
                maxSampleZ,
                brushCenter,
                radius,
                paintMaterialId,
                _preExistingSolidCells);
        }

        foreach (ChunkCoord coord in _modifiedChunks)
        {
            if (_chunkStore.TryGet(coord, out ChunkData dirtyChunk) &&
                _chunkViews.TryGetValue(coord, out ChunkView dirtyView))
            {
                RebuildDirtySubChunks(dirtyChunk, dirtyView, _applyMeshCollider);
            }
        }
    }

    private sbyte GetWorldSampleDensityOrGenerated(int worldSampleX, int sampleY, int worldSampleZ)
    {
        if (sampleY < 0 || sampleY >= WorldConstants.SampleSizeY)
        {
            return WorldConstants.EmptyDensity;
        }

        if (TryGetWorldSampleDensity(worldSampleX, sampleY, worldSampleZ, out sbyte density))
        {
            return density;
        }

        return TerrainDensityUtility.SampleDensity(_generationSettings, worldSampleX, sampleY, worldSampleZ);
    }

    private bool IsGeneratedSolidApprox(int worldX, int worldY, int worldZ)
    {
        int densitySum = 0;

        for (int offsetY = 0; offsetY <= 1; offsetY++)
        {
            for (int offsetZ = 0; offsetZ <= 1; offsetZ++)
            {
                for (int offsetX = 0; offsetX <= 1; offsetX++)
                {
                    densitySum += TerrainDensityUtility.SampleDensity(
                        _generationSettings,
                        worldX + offsetX,
                        worldY + offsetY,
                        worldZ + offsetZ);
                }
            }
        }

        return densitySum >= WorldConstants.SurfaceThreshold * 8;
    }

    private static bool IsCellSolidApprox(ChunkData chunk, int cellX, int cellY, int cellZ)
    {
        int densitySum = 0;

        for (int offsetY = 0; offsetY <= 1; offsetY++)
        {
            for (int offsetZ = 0; offsetZ <= 1; offsetZ++)
            {
                for (int offsetX = 0; offsetX <= 1; offsetX++)
                {
                    densitySum += chunk.GetDensity(cellX + offsetX, cellY + offsetY, cellZ + offsetZ);
                }
            }
        }

        return densitySum >= WorldConstants.SurfaceThreshold * 8;
    }

    private void CacheSolidCellsInBounds(
        int minSampleX,
        int maxSampleX,
        int minSampleY,
        int maxSampleY,
        int minSampleZ,
        int maxSampleZ)
    {
        int minCellX = minSampleX - 1;
        int maxCellX = maxSampleX;
        int minCellY = Mathf.Max(0, minSampleY - 1);
        int maxCellY = Mathf.Min(WorldConstants.ChunkSizeY - 1, maxSampleY);
        int minCellZ = minSampleZ - 1;
        int maxCellZ = maxSampleZ;

        for (int worldCellZ = minCellZ; worldCellZ <= maxCellZ; worldCellZ++)
        {
            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                for (int worldCellX = minCellX; worldCellX <= maxCellX; worldCellX++)
                {
                    if (IsWorldCellSolid(worldCellX, cellY, worldCellZ))
                    {
                        _preExistingSolidCells.Add(new Vector3Int(worldCellX, cellY, worldCellZ));
                    }
                }
            }
        }
    }

    private void PaintMaterialsInBounds(
        int minSampleX,
        int maxSampleX,
        int minSampleY,
        int maxSampleY,
        int minSampleZ,
        int maxSampleZ,
        Vector3 brushCenter,
        float radius,
        byte materialId,
        HashSet<Vector3Int> preExistingSolidCells)
    {
        int minCellX = minSampleX - 1;
        int maxCellX = maxSampleX;
        int minCellY = Mathf.Max(0, minSampleY - 1);
        int maxCellY = Mathf.Min(WorldConstants.ChunkSizeY - 1, maxSampleY);
        int minCellZ = minSampleZ - 1;
        int maxCellZ = maxSampleZ;
        float radiusSq = radius * radius;

        for (int worldCellZ = minCellZ; worldCellZ <= maxCellZ; worldCellZ++)
        {
            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                for (int worldCellX = minCellX; worldCellX <= maxCellX; worldCellX++)
                {
                    Vector3 cellCenter = new Vector3(worldCellX + 0.5f, cellY + 0.5f, worldCellZ + 0.5f);
                    if ((cellCenter - brushCenter).sqrMagnitude > radiusSq)
                    {
                        continue;
                    }

                    Vector3Int cellCoord = new Vector3Int(worldCellX, cellY, worldCellZ);
                    if (preExistingSolidCells.Contains(cellCoord) || !IsWorldCellSolid(worldCellX, cellY, worldCellZ))
                    {
                        continue;
                    }

                    if (!TrySetWorldCellMaterial(worldCellX, cellY, worldCellZ, materialId))
                    {
                        continue;
                    }
                }
            }
        }
    }

    private bool TrySetWorldCellMaterial(int worldX, int worldY, int worldZ, byte materialId)
    {
        if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
        {
            return false;
        }

        ChunkCoord coord = WorldMath.WorldCellToChunkCoord(worldX, worldZ);
        if (!_chunkStore.TryGet(coord, out ChunkData chunk))
        {
            return false;
        }

        Vector3Int localCell = WorldMath.WorldCellToLocalCell(worldX, worldY, worldZ);
        if (localCell.x < 0 || localCell.x >= WorldConstants.ChunkSizeX ||
            localCell.z < 0 || localCell.z >= WorldConstants.ChunkSizeZ)
        {
            return false;
        }

        if (chunk.GetMaterialId(localCell.x, localCell.y, localCell.z) == materialId)
        {
            return false;
        }

        chunk.SetMaterialId(localCell.x, localCell.y, localCell.z, materialId);
        chunk.MarkSubChunkDirty(WorldMath.CellYToSubChunkIndex(localCell.y));
        _modifiedChunks.Add(coord);
        return true;
    }

    private bool IsWorldCellSolid(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
        {
            return false;
        }

        if (!TryGetWorldSampleDensity(worldX + 0, worldY + 0, worldZ + 0, out sbyte d0) ||
            !TryGetWorldSampleDensity(worldX + 1, worldY + 0, worldZ + 0, out sbyte d1) ||
            !TryGetWorldSampleDensity(worldX + 1, worldY + 0, worldZ + 1, out sbyte d2) ||
            !TryGetWorldSampleDensity(worldX + 0, worldY + 0, worldZ + 1, out sbyte d3) ||
            !TryGetWorldSampleDensity(worldX + 0, worldY + 1, worldZ + 0, out sbyte d4) ||
            !TryGetWorldSampleDensity(worldX + 1, worldY + 1, worldZ + 0, out sbyte d5) ||
            !TryGetWorldSampleDensity(worldX + 1, worldY + 1, worldZ + 1, out sbyte d6) ||
            !TryGetWorldSampleDensity(worldX + 0, worldY + 1, worldZ + 1, out sbyte d7))
        {
            return false;
        }

        int cubeIndex = 0;
        if (MarchingCubesCommon.IsInside(d0)) cubeIndex |= 1 << 0;
        if (MarchingCubesCommon.IsInside(d1)) cubeIndex |= 1 << 1;
        if (MarchingCubesCommon.IsInside(d2)) cubeIndex |= 1 << 2;
        if (MarchingCubesCommon.IsInside(d3)) cubeIndex |= 1 << 3;
        if (MarchingCubesCommon.IsInside(d4)) cubeIndex |= 1 << 4;
        if (MarchingCubesCommon.IsInside(d5)) cubeIndex |= 1 << 5;
        if (MarchingCubesCommon.IsInside(d6)) cubeIndex |= 1 << 6;
        if (MarchingCubesCommon.IsInside(d7)) cubeIndex |= 1 << 7;

        return cubeIndex != 0;
    }

    private void ModifySharedSamplesInBounds(
        int minSampleX,
        int maxSampleX,
        int minSampleY,
        int maxSampleY,
        int minSampleZ,
        int maxSampleZ,
        Vector3 brushCenter,
        Vector3 surfaceNormal,
        float radius,
        int deltaAmount)
    {
        _ = surfaceNormal;

        for (int worldSampleZ = minSampleZ; worldSampleZ <= maxSampleZ; worldSampleZ++)
        {
            for (int sampleY = minSampleY; sampleY <= maxSampleY; sampleY++)
            {
                for (int worldSampleX = minSampleX; worldSampleX <= maxSampleX; worldSampleX++)
                {
                    Vector3 sampleWorldPosition = new Vector3(worldSampleX, sampleY, worldSampleZ);
                    if (!TryGetWorldSampleDensity(worldSampleX, sampleY, worldSampleZ, out sbyte currentDensity))
                    {
                        continue;
                    }

                    int delta = EvaluateSurfaceBrushDelta(
                        sampleWorldPosition,
                        brushCenter,
                        radius,
                        currentDensity,
                        deltaAmount);

                    if (delta == 0)
                    {
                        continue;
                    }

                    ApplyDeltaToSharedSample(worldSampleX, sampleY, worldSampleZ, delta);
                }
            }
        }
    }

    private static int EvaluateSurfaceBrushDelta(
        Vector3 sampleWorldPosition,
        Vector3 brushCenter,
        float radius,
        sbyte currentDensity,
        int deltaAmount)
    {
        float distance = Vector3.Distance(sampleWorldPosition, brushCenter);
        if (distance > radius)
        {
            return 0;
        }

        float radialWeight = 1f - (distance / Mathf.Max(0.0001f, radius));
        float thresholdDistance = Mathf.Abs(currentDensity - WorldConstants.SurfaceThreshold) / 127f;
        float thresholdWeight = 1f - Mathf.Clamp01(thresholdDistance);
        float combinedWeight = Mathf.Max(radialWeight, thresholdWeight * 0.5f);

        int delta = Mathf.RoundToInt(deltaAmount * combinedWeight);
        if (delta == 0)
        {
            delta = deltaAmount > 0 ? 1 : -1;
        }

        return delta;
    }

    private bool TryGetWorldSampleDensity(int worldSampleX, int sampleY, int worldSampleZ, out sbyte density)
    {
        density = WorldConstants.EmptyDensity;

        if (sampleY < 0 || sampleY >= WorldConstants.SampleSizeY)
        {
            return false;
        }

        ChunkCoord coord = WorldMath.WorldSampleToChunkCoord(worldSampleX, worldSampleZ);
        if (_pendingGenerations.ContainsKey(coord))
        {
            return false;
        }

        if (!_chunkStore.TryGet(coord, out ChunkData chunk))
        {
            return false;
        }

        int localSampleX = worldSampleX - coord.X * WorldConstants.ChunkSizeX;
        int localSampleZ = worldSampleZ - coord.Z * WorldConstants.ChunkSizeZ;

        if (localSampleX < 0 || localSampleX >= WorldConstants.SampleSizeX ||
            localSampleZ < 0 || localSampleZ >= WorldConstants.SampleSizeZ)
        {
            return false;
        }

        density = chunk.GetDensity(localSampleX, sampleY, localSampleZ);
        return true;
    }

    private void ApplyDeltaToSharedSample(int worldSampleX, int sampleY, int worldSampleZ, int delta)
    {
        bool isBoundaryX = WorldMath.PositiveMod(worldSampleX, WorldConstants.ChunkSizeX) == 0;
        bool isBoundaryZ = WorldMath.PositiveMod(worldSampleZ, WorldConstants.ChunkSizeZ) == 0;

        int baseChunkX = WorldMath.FloorDiv(worldSampleX, WorldConstants.ChunkSizeX);
        int baseChunkZ = WorldMath.FloorDiv(worldSampleZ, WorldConstants.ChunkSizeZ);

        int minChunkX = isBoundaryX ? baseChunkX - 1 : baseChunkX;
        int maxChunkX = baseChunkX;
        int minChunkZ = isBoundaryZ ? baseChunkZ - 1 : baseChunkZ;
        int maxChunkZ = baseChunkZ;

        bool newValueComputed = false;
        sbyte newValue = 0;

        for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
        {
            for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                ChunkCoord coord = new ChunkCoord(chunkX, chunkZ);
                if (!_chunkStore.TryGet(coord, out ChunkData chunk))
                {
                    continue;
                }

                int localSampleX = worldSampleX - chunkX * WorldConstants.ChunkSizeX;
                int localSampleZ = worldSampleZ - chunkZ * WorldConstants.ChunkSizeZ;

                if (localSampleX < 0 || localSampleX >= WorldConstants.SampleSizeX ||
                    localSampleZ < 0 || localSampleZ >= WorldConstants.SampleSizeZ)
                {
                    continue;
                }

                if (!newValueComputed)
                {
                    sbyte oldValue = chunk.GetDensity(localSampleX, sampleY, localSampleZ);
                    newValue = (sbyte)Mathf.Clamp(oldValue + delta, WorldConstants.EmptyDensity, WorldConstants.FullDensity);
                    newValueComputed = true;
                }

                if (chunk.GetDensity(localSampleX, sampleY, localSampleZ) == newValue)
                {
                    continue;
                }

                chunk.SetDensity(localSampleX, sampleY, localSampleZ, newValue);
                MarkSampleAffectedSubChunks(chunk, sampleY);
                _modifiedChunks.Add(coord);
            }
        }
    }

    private static void MarkSampleAffectedSubChunks(ChunkData chunk, int sampleY)
    {
        if (sampleY <= 0)
        {
            chunk.MarkSubChunkDirty(0);
            return;
        }

        int cellYUpper = Mathf.Clamp(sampleY, 0, WorldConstants.ChunkSizeY - 1);
        int cellYLower = Mathf.Clamp(sampleY - 1, 0, WorldConstants.ChunkSizeY - 1);

        chunk.MarkSubChunkDirty(WorldMath.CellYToSubChunkIndex(cellYUpper));
        chunk.MarkSubChunkDirty(WorldMath.CellYToSubChunkIndex(cellYLower));
    }

    private void CompleteAndDisposePendingJobs()
    {
        foreach (PendingChunkGeneration operation in _pendingGenerations.Values)
        {
            operation.Handle.Complete();
        }

        _pendingGenerations.Clear();

        foreach (PendingChunkMeshBuild build in _pendingChunkMeshBuilds.Values)
        {
            if (build.SubChunkBuilds == null)
            {
                continue;
            }

            for (int i = 0; i < build.SubChunkBuilds.Length; i++)
            {
                PendingSubChunkBuild operation = build.SubChunkBuilds[i];
                if (operation == null)
                {
                    continue;
                }

                operation.Handle.Complete();
            }

            build.Dispose();
        }

        _pendingChunkMeshBuilds.Clear();

        foreach (PendingProxyMeshBuild build in _pendingProxyMeshBuilds.Values)
        {
            build.Handle.Complete();
            build.Dispose();
        }

        _pendingProxyMeshBuilds.Clear();
    }

    private bool HasPendingJobsForChunk(ChunkCoord coord)
    {
        return _pendingGenerations.ContainsKey(coord) || _pendingChunkMeshBuilds.ContainsKey(coord);
    }

    private bool HasPendingJobsForProxy(ProxyCoord coord)
    {
        return _pendingProxyMeshBuilds.ContainsKey(coord);
    }

    private static int GetHorizontalStepForLod(int lodLevel)
    {
        switch (lodLevel)
        {
            case LOD1:
                return 2;
            case LOD2:
                return 4;
            default:
                return 1;
        }
    }

    private static int GetProxyChunkSpanForLod(int lodLevel)
    {
        switch (lodLevel)
        {
            case LOD1:
                return 2;
            case LOD2:
                return 4;
            default:
                return 1;
        }
    }

    private void GetBaseChunkRect(ChunkCoord center, out int minChunkX, out int maxChunkXExclusive, out int minChunkZ, out int maxChunkZExclusive)
    {
        int diameter = BaseChunkDiameter;
        int half = diameter / 2;
        minChunkX = WorldMath.AlignDown(center.X - half, 4);
        minChunkZ = WorldMath.AlignDown(center.Z - half, 4);
        maxChunkXExclusive = minChunkX + diameter;
        maxChunkZExclusive = minChunkZ + diameter;
    }

    private void GetFiniteChunkRect(out int minChunkX, out int maxChunkXExclusive, out int minChunkZ, out int maxChunkZExclusive)
    {
        int diameter = FiniteChunkDiameter;
        int half = diameter / 2;
        minChunkX = -half;
        minChunkZ = -half;
        maxChunkXExclusive = minChunkX + diameter;
        maxChunkZExclusive = minChunkZ + diameter;
    }

    public bool TryGetLodBoundaryRect(LodBoundaryKind kind, out int minChunkX, out int maxChunkXExclusive, out int minChunkZ, out int maxChunkZExclusive)
    {
        minChunkX = 0;
        maxChunkXExclusive = 0;
        minChunkZ = 0;
        maxChunkZExclusive = 0;

        if (!_hasStreamingCenter)
        {
            return false;
        }

        if (_finiteMap)
        {
            if (kind != LodBoundaryKind.Lod0Outer)
            {
                return false;
            }

            GetFiniteChunkRect(out minChunkX, out maxChunkXExclusive, out minChunkZ, out maxChunkZExclusive);
            return true;
        }

        GetBaseChunkRect(_streamingCenter, out int baseMinX, out int baseMaxX, out int baseMinZ, out int baseMaxZ);
        int proxy1MinX = baseMinX - Proxy1Border;
        int proxy1MaxX = baseMaxX + Proxy1Border;
        int proxy1MinZ = baseMinZ - Proxy1Border;
        int proxy1MaxZ = baseMaxZ + Proxy1Border;
        int proxy2MinX = proxy1MinX - Proxy2Border;
        int proxy2MaxX = proxy1MaxX + Proxy2Border;
        int proxy2MinZ = proxy1MinZ - Proxy2Border;
        int proxy2MaxZ = proxy1MaxZ + Proxy2Border;

        switch (kind)
        {
            case LodBoundaryKind.Lod0Outer:
                minChunkX = baseMinX;
                maxChunkXExclusive = baseMaxX;
                minChunkZ = baseMinZ;
                maxChunkZExclusive = baseMaxZ;
                return true;
            case LodBoundaryKind.Lod1Outer:
                minChunkX = proxy1MinX;
                maxChunkXExclusive = proxy1MaxX;
                minChunkZ = proxy1MinZ;
                maxChunkZExclusive = proxy1MaxZ;
                return true;
            case LodBoundaryKind.Lod2Outer:
                minChunkX = proxy2MinX;
                maxChunkXExclusive = proxy2MaxX;
                minChunkZ = proxy2MinZ;
                maxChunkZExclusive = proxy2MaxZ;
                return true;
            default:
                return false;
        }
    }

    private void AddDesiredProxies(
        int lodLevel,
        int alignment,
        int outerMinX,
        int outerMaxX,
        int outerMinZ,
        int outerMaxZ,
        int innerMinX,
        int innerMaxX,
        int innerMinZ,
        int innerMaxZ)
    {
        int span = GetProxyChunkSpanForLod(lodLevel);
        int iterMinX = WorldMath.AlignDown(outerMinX, alignment);
        int iterMaxX = WorldMath.AlignUp(outerMaxX, alignment);
        int iterMinZ = WorldMath.AlignDown(outerMinZ, alignment);
        int iterMaxZ = WorldMath.AlignUp(outerMaxZ, alignment);

        for (int z = iterMinZ; z < iterMaxZ; z += alignment)
        {
            for (int x = iterMinX; x < iterMaxX; x += alignment)
            {
                int proxyMaxX = x + span;
                int proxyMaxZ = z + span;
                bool intersectsOuter = RectsIntersect(x, proxyMaxX, z, proxyMaxZ, outerMinX, outerMaxX, outerMinZ, outerMaxZ);
                bool intersectsInner = RectsIntersect(x, proxyMaxX, z, proxyMaxZ, innerMinX, innerMaxX, innerMinZ, innerMaxZ);

                if (!intersectsOuter || intersectsInner)
                {
                    continue;
                }

                _desiredProxyCoords.Add(new ProxyCoord(x, z, lodLevel));
            }
        }
    }

    private void AdvanceInitialStreamingPhase()
    {
        if (!_hasStreamingCenter)
        {
            return;
        }

        if (_finiteMap || !RenderProxyChunks)
        {
            if (_initialStreamingPhase == InitialStreamingPhase.Lod0Only && AreAllDesiredLod0ViewsReady())
            {
                _initialStreamingPhase = InitialStreamingPhase.Complete;
            }

            return;
        }

        if (_initialStreamingPhase == InitialStreamingPhase.Lod0Only && AreAllDesiredLod0ViewsReady())
        {
            _initialStreamingPhase = InitialStreamingPhase.Proxy1;
            RefreshDesiredChunkSet(_streamingCenter);
            return;
        }

        if (_initialStreamingPhase == InitialStreamingPhase.Proxy1 && AreAllDesiredProxyViewsReady(LOD1))
        {
            _initialStreamingPhase = InitialStreamingPhase.Proxy2;
            RefreshDesiredChunkSet(_streamingCenter);
            return;
        }

        if (_initialStreamingPhase == InitialStreamingPhase.Proxy2 && AreAllDesiredProxyViewsReady(LOD2))
        {
            _initialStreamingPhase = InitialStreamingPhase.Complete;
        }
    }

    private bool IsProxyLoadAllowedInCurrentPhase(int lodLevel)
    {
        return _initialStreamingPhase switch
        {
            InitialStreamingPhase.Lod0Only => false,
            InitialStreamingPhase.Proxy1 => lodLevel == LOD1,
            _ => true
        };
    }

    private bool AreAllDesiredLod0ViewsReady()
    {
        if (_pendingChunkLoads.Count > 0 || _pendingGenerations.Count > 0 || _pendingChunkMeshBuilds.Count > 0)
        {
            return false;
        }

        foreach (ChunkCoord coord in _desiredChunkCoords)
        {
            if (!_chunkViews.ContainsKey(coord))
            {
                return false;
            }
        }

        return true;
    }

    private bool AreAllDesiredProxyViewsReady(int lodLevel)
    {
        if (HasPendingProxyWork(lodLevel))
        {
            return false;
        }

        foreach (ProxyCoord coord in _desiredProxyCoords)
        {
            if (coord.LodLevel != lodLevel)
            {
                continue;
            }

            if (!_proxyViews.ContainsKey(coord))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasPendingProxyWork(int lodLevel)
    {
        foreach (ProxyCoord coord in _pendingProxyLoadSet)
        {
            if (coord.LodLevel == lodLevel)
            {
                return true;
            }
        }

        foreach (KeyValuePair<ProxyCoord, PendingProxyMeshBuild> pair in _pendingProxyMeshBuilds)
        {
            if (pair.Key.LodLevel == lodLevel)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RectsIntersect(
        int minAx,
        int maxAx,
        int minAz,
        int maxAz,
        int minBx,
        int maxBx,
        int minBz,
        int maxBz)
    {
        return minAx < maxBx && maxAx > minBx && minAz < maxBz && maxAz > minBz;
    }

    private static int ChunkDistanceSq(ChunkCoord coord, ChunkCoord center)
    {
        int dx = coord.X - center.X;
        int dz = coord.Z - center.Z;
        return dx * dx + dz * dz;
    }

    private static int ProxyDistanceSq(ProxyCoord coord, ChunkCoord center)
    {
        int span = GetProxyChunkSpanForLod(coord.LodLevel);
        int proxyCenterX = coord.X + span / 2;
        int proxyCenterZ = coord.Z + span / 2;
        int dx = proxyCenterX - center.X;
        int dz = proxyCenterZ - center.Z;
        return dx * dx + dz * dz;
    }

    private static int SanitizePositiveMultiple(int value, int multiple)
    {
        int clamped = Mathf.Max(multiple, value);
        int remainder = clamped % multiple;
        return remainder == 0 ? clamped : clamped + (multiple - remainder);
    }

    public enum LodBoundaryKind
    {
        Lod0Outer = 0,
        Lod1Outer = 1,
        Lod2Outer = 2
    }
}
