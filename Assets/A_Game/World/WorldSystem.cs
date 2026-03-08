using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Owns chunk data, chunk views, streaming, generation, meshing, and density edits.
/// </summary>
public sealed class WorldSystem : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private TerrainGenerationSettings _generationSettings = new TerrainGenerationSettings
    {
        NoiseScale = 0.02f,
        BaseHeight = 48f,
        HeightAmplitude = 64f,
        SurfaceFade = 8f
    };

    [Header("Streaming")]
    [SerializeField] private Transform _streamingTarget;
    [SerializeField] private int _loadRadius = 8;
    [SerializeField] private int _maxChunkLoadsPerFrame = 2;
    [SerializeField] private int _maxChunkUnloadsPerFrame = 4;

    [Header("View")]
    [SerializeField] private ChunkView _chunkViewPrefab;
    [SerializeField] private Transform _chunkRoot;
    [SerializeField] private Material _terrainMaterial;
    [SerializeField] private TerrainMaterialLibrary _terrainMaterialLibrary;
    [SerializeField] private bool _applyMeshCollider = true;

    private sealed class PendingChunkGeneration
    {
        public ChunkCoord Coord;
        public JobHandle Handle;
    }

    private sealed class PendingSubChunkBuild : IDisposable
    {
        public int SubChunkIndex;
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
        public ChunkView View;
        public bool ApplyCollider;
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

    private readonly ChunkDataStore _chunkStore = new ChunkDataStore();
    private readonly Dictionary<ChunkCoord, ChunkView> _chunkViews = new Dictionary<ChunkCoord, ChunkView>();
    private readonly HashSet<ChunkCoord> _modifiedChunks = new HashSet<ChunkCoord>();
    private readonly HashSet<ChunkCoord> _desiredChunkCoords = new HashSet<ChunkCoord>();
    private readonly Queue<ChunkCoord> _pendingChunkLoads = new Queue<ChunkCoord>();
    private readonly Queue<ChunkCoord> _pendingChunkUnloads = new Queue<ChunkCoord>();
    private readonly HashSet<ChunkCoord> _pendingLoadSet = new HashSet<ChunkCoord>();
    private readonly HashSet<ChunkCoord> _pendingUnloadSet = new HashSet<ChunkCoord>();
    private readonly Dictionary<ChunkCoord, PendingChunkGeneration> _pendingGenerations = new Dictionary<ChunkCoord, PendingChunkGeneration>();
    private readonly Dictionary<ChunkCoord, PendingChunkMeshBuild> _pendingChunkMeshBuilds = new Dictionary<ChunkCoord, PendingChunkMeshBuild>();
    private readonly List<ChunkCoord> _coordBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _loadBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _unloadBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _deferredUnloadBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _completedGenerationBuffer = new List<ChunkCoord>();
    private readonly List<ChunkCoord> _completedChunkMeshBuffer = new List<ChunkCoord>();
    private readonly Stack<ChunkView> _chunkViewPool = new Stack<ChunkView>();

    private bool _hasStreamingCenter;
    private ChunkCoord _streamingCenter;

    public int LoadedChunkCount => _chunkStore.Count;
    public int PendingChunkLoadCount => _pendingChunkLoads.Count;
    public int PendingGenerationCount => _pendingGenerations.Count;
    public int PendingMeshBuildCount => _pendingChunkMeshBuilds.Count;
    public int PendingChunkUnloadCount => _pendingChunkUnloads.Count;
    public int PooledChunkViewCount => _chunkViewPool.Count;
    public bool HasGeneratedWorld { get; private set; }
    public TerrainMaterialLibrary TerrainMaterialLibrary => _terrainMaterialLibrary;
    private int MaxChunkViewPoolCount => ChunkCountForRadius(Mathf.Max(0, _loadRadius) + 2);

    [ContextMenu("Generate Initial World")]
    public void GenerateInitialWorld()
    {
        EnsureTerrainMaterialConfigured();
        ClearWorld();

        HasGeneratedWorld = true;
        RefreshStreamingCenter(true);
        ProcessQueuedOperations(
            Mathf.Max(0, _maxChunkLoadsPerFrame),
            Mathf.Max(0, _maxChunkUnloadsPerFrame));
        ProcessCompletedGenerationJobs();
        ProcessCompletedChunkMeshBuilds();
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

        while (_chunkViewPool.Count > 0)
        {
            ChunkView pooledView = _chunkViewPool.Pop();
            if (pooledView != null)
            {
                DestroyChunkView(pooledView);
            }
        }

        _chunkViews.Clear();
        _chunkStore.ClearAndDispose();
        _desiredChunkCoords.Clear();
        _pendingChunkLoads.Clear();
        _pendingChunkUnloads.Clear();
        _pendingLoadSet.Clear();
        _pendingUnloadSet.Clear();
        _coordBuffer.Clear();
        _loadBuffer.Clear();
        _unloadBuffer.Clear();
        _deferredUnloadBuffer.Clear();
        _completedGenerationBuffer.Clear();
        _completedChunkMeshBuffer.Clear();
        _modifiedChunks.Clear();
        _hasStreamingCenter = false;
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
            deltaAmount);
    }

    public void ApplySphereBrush(Vector3 worldPosition, float radius, int deltaAmount)
    {
        ApplyOrientedBrush(worldPosition, Vector3.up, radius, deltaAmount);
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
        ProcessQueuedOperations(
            Mathf.Max(0, _maxChunkLoadsPerFrame),
            Mathf.Max(0, _maxChunkUnloadsPerFrame));
        ProcessCompletedGenerationJobs();
        ProcessCompletedChunkMeshBuilds();
    }

    private void OnDestroy()
    {
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
        int loadRadius = Mathf.Max(0, _loadRadius);

        for (int dz = -loadRadius; dz <= loadRadius; dz++)
        {
            for (int dx = -loadRadius; dx <= loadRadius; dx++)
            {
                _desiredChunkCoords.Add(new ChunkCoord(center.X + dx, center.Z + dz));
            }
        }

        _loadBuffer.Clear();
        foreach (ChunkCoord coord in _desiredChunkCoords)
        {
            if (_chunkStore.Contains(coord))
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

        _coordBuffer.Clear();
        foreach (KeyValuePair<ChunkCoord, ChunkData> pair in _chunkStore.Enumerate())
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

    private void ProcessQueuedOperations(int loadBudget, int unloadBudget)
    {
        int unloaded = 0;
        _deferredUnloadBuffer.Clear();

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

        for (int i = 0; i < _deferredUnloadBuffer.Count; i++)
        {
            EnqueueChunkUnload(_deferredUnloadBuffer[i]);
        }

        int loaded = 0;
        while (loaded < loadBudget && _pendingChunkLoads.Count > 0)
        {
            ChunkCoord coord = _pendingChunkLoads.Dequeue();
            _pendingLoadSet.Remove(coord);

            if (!_desiredChunkCoords.Contains(coord) || _chunkStore.Contains(coord))
            {
                continue;
            }

            GenerateChunk(coord);
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
            Density = chunk.Density,
            MaterialIds = chunk.MaterialIds
        };

        PendingChunkGeneration operation = new PendingChunkGeneration
        {
            Coord = coord,
            Handle = generationJob.Schedule(WorldConstants.ChunkSampleCount, 128)
        };

        _pendingGenerations.Add(coord, operation);
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

            if (!_desiredChunkCoords.Contains(coord))
            {
                _chunkStore.RemoveAndDispose(coord);
                continue;
            }

            Transform root = _chunkRoot != null ? _chunkRoot : transform;
            ChunkView view = AcquireChunkView(coord, root);
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
        foreach (KeyValuePair<ChunkCoord, PendingChunkMeshBuild> pair in _pendingChunkMeshBuilds)
        {
            if (TryAdvanceChunkMeshBuild(pair.Value))
            {
                _completedChunkMeshBuffer.Add(pair.Key);
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

    private void UnloadChunk(ChunkCoord coord)
    {
        _chunkStore.RemoveAndDispose(coord);

        if (_chunkViews.TryGetValue(coord, out ChunkView view))
        {
            _chunkViews.Remove(coord);
            ReleaseChunkView(view);
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
        view.ClearAllSubChunks();
        view.gameObject.SetActive(false);

        PendingChunkMeshBuild build = new PendingChunkMeshBuild
        {
            Coord = chunk.Coord,
            View = view,
            ApplyCollider = applyCollider,
            SubChunkBuilds = new PendingSubChunkBuild[WorldConstants.SubChunkCount]
        };

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

    private bool TryAdvanceChunkMeshBuild(PendingChunkMeshBuild build)
    {
        bool allReady = true;

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
                    operation.ReadyToApply = true;
                    continue;
                }

                operation.MeshData = new SubChunkMeshData(totalTriangles, Allocator.Persistent);

                SubChunkMeshWriteJob writeJob = new SubChunkMeshWriteJob
                {
                    Density = chunk.Density,
                    MaterialIds = chunk.MaterialIds,
                    TriangleCounts = operation.TriangleCounts,
                    TriangleOffsets = operation.TriangleOffsets,
                    SubChunkIndex = operation.SubChunkIndex,
                    Vertices = operation.MeshData.Vertices,
                    Indices = operation.MeshData.Indices,
                    MaterialInfo = operation.MeshData.MaterialInfo
                };

                operation.Handle = writeJob.Schedule(WorldConstants.SubChunkCellCount, 128);
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
            bool isDesired = _desiredChunkCoords.Contains(build.Coord);
            bool hasChunk = _chunkStore.TryGet(build.Coord, out ChunkData chunk);
            bool hasView = _chunkViews.TryGetValue(build.Coord, out ChunkView currentView);
            bool shouldApply = isDesired && hasChunk && hasView && currentView == build.View;

            if (!shouldApply)
            {
                if (hasView && currentView == build.View)
                {
                    _chunkViews.Remove(build.Coord);
                    ReleaseChunkView(currentView);
                }

                if (!isDesired && hasChunk)
                {
                    UnloadChunk(build.Coord);
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

                chunk.ClearSubChunkDirty(operation.SubChunkIndex);
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
                    Vertices = meshData.Vertices,
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
        int deltaAmount)
    {
        _modifiedChunks.Clear();
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

        foreach (ChunkCoord coord in _modifiedChunks)
        {
            if (_chunkStore.TryGet(coord, out ChunkData dirtyChunk) &&
                _chunkViews.TryGetValue(coord, out ChunkView dirtyView))
            {
                RebuildDirtySubChunks(dirtyChunk, dirtyView, _applyMeshCollider);
            }
        }
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
                    if (!TryGetWorldSampleDensity(worldSampleX, sampleY, worldSampleZ, out byte currentDensity))
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
        byte currentDensity,
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

    private bool TryGetWorldSampleDensity(int worldSampleX, int sampleY, int worldSampleZ, out byte density)
    {
        density = WorldConstants.EmptyDensity;

        if (sampleY < 0 || sampleY >= WorldConstants.SampleSizeY)
        {
            return false;
        }

        ChunkCoord coord = WorldMath.WorldSampleToChunkCoord(worldSampleX, worldSampleZ);
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
        byte newValue = 0;

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
                    byte oldValue = chunk.GetDensity(localSampleX, sampleY, localSampleZ);
                    newValue = (byte)Mathf.Clamp(oldValue + delta, WorldConstants.EmptyDensity, WorldConstants.FullDensity);
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
    }

    private bool HasPendingJobsForChunk(ChunkCoord coord)
    {
        return _pendingGenerations.ContainsKey(coord) || _pendingChunkMeshBuilds.ContainsKey(coord);
    }

    private static int ChunkDistanceSq(ChunkCoord coord, ChunkCoord center)
    {
        int dx = coord.X - center.X;
        int dz = coord.Z - center.Z;
        return dx * dx + dz * dz;
    }

    private static int ChunkCountForRadius(int radius)
    {
        int diameter = radius * 2 + 1;
        return diameter * diameter;
    }
}
