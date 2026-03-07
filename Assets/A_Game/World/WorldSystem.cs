using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 월드 전체 흐름을 관리하는 중심 시스템이다.
///
/// 이 클래스는 다음 책임을 가진다.
/// - 청크 데이터 생성과 보관
/// - 청크 뷰 생성과 배치
/// - 지형 생성 Job 실행
/// - dirty 서브청크 재메시
/// - density 브러시 편집과 영향 청크 갱신
/// </summary>
public sealed class WorldSystem : MonoBehaviour
{
    [Header("월드 생성 범위")]
    [SerializeField] private int _countX = 10;
    [SerializeField] private int _countZ = 10;

    [Header("생성 설정")]
    [SerializeField] private TerrainGenerationSettings _generationSettings = new TerrainGenerationSettings
    {
        NoiseScale = 0.02f,
        BaseHeight = 48f,
        HeightAmplitude = 64f,
        SurfaceFade = 8f
    };

    [Header("뷰 설정")]
    [SerializeField] private ChunkView _chunkViewPrefab;
    [SerializeField] private Transform _chunkRoot;
    [SerializeField] private Material _terrainMaterial;
    [SerializeField] private bool _applyMeshCollider = true;

    private readonly ChunkDataStore _chunkStore = new ChunkDataStore();
    private readonly Dictionary<ChunkCoord, ChunkView> _chunkViews = new Dictionary<ChunkCoord, ChunkView>();
    private readonly HashSet<ChunkCoord> _modifiedChunks = new HashSet<ChunkCoord>();

    public int LoadedChunkCount => _chunkStore.Count;
    public bool HasGeneratedWorld { get; private set; }

    [ContextMenu("Generate Initial World")]
    public void GenerateInitialWorld()
    {
        ClearWorld();

        Transform root = _chunkRoot != null ? _chunkRoot : transform;

        for (int z = 0; z < _countZ; z++)
        {
            for (int x = 0; x < _countX; x++)
            {
                GenerateChunk(new ChunkCoord(x, z), root);
            }
        }

        HasGeneratedWorld = true;
    }

    [ContextMenu("Clear World")]
    public void ClearWorld()
    {
        foreach (ChunkView view in _chunkViews.Values)
        {
            if (view == null)
            {
                continue;
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

        _chunkViews.Clear();
        _chunkStore.ClearAndDispose();
        HasGeneratedWorld = false;
    }

    public bool TryGetChunk(ChunkCoord coord, out ChunkData chunk)
    {
        return _chunkStore.TryGet(coord, out chunk);
    }

    /// <summary>
    /// 현재 로드된 모든 청크를 density 원본 기준으로 강제 리빌드한다.
    ///
    /// 이 메서드는 디버그용으로 유용하다.
    /// 편집 후 구멍이나 seam이 보일 때, 같은 density 데이터로 다시 메시를 만들었는데도
    /// 문제가 남는지 확인하면 원인이 편집기인지 메셔인지 분리하기 쉬워진다.
    /// </summary>
    [ContextMenu("Rebuild All Loaded Chunks")]
    public void RebuildAllLoadedChunks()
    {
        RebuildAllLoadedChunks(true);
    }

    /// <summary>
    /// 현재 로드된 모든 청크를 강제로 다시 메시화한다.
    /// 필요에 따라 collider 갱신을 끌 수 있다.
    /// </summary>
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

    /// <summary>
    /// 화면 중앙에서 맞춘 지점을 기준으로 density 브러시를 적용한다.
    ///
    /// 현재 편집 방식은 예시 프로젝트와 비슷하게 "반경 안의 density를 직접 수정"하는 구조다.
    /// 즉 메시를 직접 깎거나 표면 샘플만 따로 고르는 것이 아니라,
    /// 히트 지점 주변 density field 전체를 부드럽게 밀고 당긴다.
    ///
    /// 이 방식의 장점은 다음과 같다.
    /// - 생성과 파괴의 반응이 더 대칭적이다.
    /// - 이미 편집한 지형을 반대로 편집할 때 속도 차이가 덜하다.
    /// - 표면 샘플만 억지로 고르지 않으므로 중간에 메워지지 않는 구멍이 줄어든다.
    /// </summary>
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

    private void OnDestroy()
    {
        _chunkStore.Dispose();
    }

    private void GenerateChunk(ChunkCoord coord, Transform root)
    {
        ChunkData chunk = new ChunkData(coord);
        _chunkStore.Add(chunk);

        TerrainGenerationJob generationJob = new TerrainGenerationJob
        {
            Coord = coord,
            Settings = _generationSettings,
            Density = chunk.Density
        };

        JobHandle generationHandle = generationJob.Schedule(WorldConstants.ChunkSampleCount, 128);
        generationHandle.Complete();

        ChunkView view = CreateChunkView(coord, root);
        _chunkViews.Add(coord, view);

        chunk.MarkAllSubChunksDirty();
        RebuildDirtySubChunks(chunk, view, _applyMeshCollider);
    }

    private ChunkView CreateChunkView(ChunkCoord coord, Transform root)
    {
        ChunkView view;

        if (_chunkViewPrefab != null)
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
                    TriangleCounts = triangleCounts,
                    TriangleOffsets = triangleOffsets,
                    SubChunkIndex = subChunkIndex,
                    Vertices = meshData.Vertices,
                    Indices = meshData.Indices
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

    /// <summary>
    /// 주어진 월드 샘플 AABB와 겹치는 청크만 순회하면서 density를 수정한다.
    ///
    /// 이 메서드는 편집 쿼리의 바깥 루프다.
    /// 브러시 반경과 겹치는 청크만 좁혀서 순회해야 불필요한 샘플 검사를 줄일 수 있다.
    /// </summary>
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

    /// <summary>
    /// 브러시 AABB 안의 월드 샘플을 한 번씩만 순회하며 density를 수정한다.
    ///
    /// 중요한 이유는 density 샘플이 청크 경계에서 인접 청크와 공유되기 때문이다.
    /// 샘플을 청크별로 순회하면 같은 월드 샘플이 경계에서 두 번 수정될 수 있다.
    ///
    /// 여기서는 월드 샘플을 기준으로 한 번만 판단하고,
    /// 값이 바뀌면 그 샘플을 공유하는 모든 청크에 동일하게 기록한다.
    /// </summary>
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

    /// <summary>
    /// 브러시 중심점 반경 안에 있는 샘플에 대해 기본적인 거리 기반 delta를 계산한다.
    ///
    /// 이 함수는 "얼마나 강하게 바꿀지"만 계산한다.
    /// 실제로 수정 가능한 surface 샘플인지는 별도 검사에서 판정한다.
    /// </summary>
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

        // threshold 근처 샘플일수록 변화량을 조금 더 우대한다.
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

    /// <summary>
    /// 월드 샘플 좌표 하나의 density를 읽는다.
    ///
    /// 청크 경계 샘플은 인접 청크와 공유되므로,
    /// 월드 샘플 좌표를 대표 청크로 변환한 뒤 그 청크의 로컬 샘플 좌표로 다시 읽는다.
    /// </summary>
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

    /// <summary>
    /// 월드 샘플 하나의 density 변화를 그 샘플을 공유하는 모든 청크에 반영한다.
    ///
    /// 예를 들어 X 또는 Z가 청크 경계에 놓인 샘플은 최대 4개 청크가 동시에 공유할 수 있다.
    /// 이 메서드는 그 모든 청크를 동일한 새 값으로 맞춰 seam을 방지한다.
    /// </summary>
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

    /// <summary>
    /// 청크 내부 로컬 샘플 하나에 density 변화를 적용한다.
    /// 실제 값이 바뀐 경우에만 true를 반환한다.
    /// </summary>
    private static bool ApplyDeltaToLocalSample(ChunkData chunk, int localSampleX, int sampleY, int localSampleZ, int delta)
    {
        byte oldValue = chunk.GetDensity(localSampleX, sampleY, localSampleZ);
        int newValue = Mathf.Clamp(oldValue + delta, WorldConstants.EmptyDensity, WorldConstants.FullDensity);

        if (newValue == oldValue)
        {
            return false;
        }

        chunk.SetDensity(localSampleX, sampleY, localSampleZ, (byte)newValue);
        MarkSampleAffectedSubChunks(chunk, sampleY);
        return true;
    }

    /// <summary>
    /// 샘플 하나가 바뀌었을 때 그 샘플을 참조하는 서브청크를 dirty 처리한다.
    ///
    /// 샘플은 위아래 두 셀 층에 동시에 영향을 줄 수 있으므로
    /// sampleY와 sampleY - 1이 속한 서브청크를 함께 표시한다.
    /// </summary>
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
}
