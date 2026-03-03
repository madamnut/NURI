// Assets/Scripts/World/WorldBootstrap10x10.cs
// 교체 범위: 파일 전체

using System.Collections.Generic;
using UnityEngine;

public sealed class WorldBootstrap10x10 : MonoBehaviour
{
    [Header("프리팹/머티리얼")]
    [SerializeField] private Chunk _chunkPrefab;
    [SerializeField] private Material _terrainMaterial;

    [Header("생성 범위(청크 개수)")]
    [SerializeField] private int _countX = 10;
    [SerializeField] private int _countZ = 10;

    [Header("높이맵 노이즈")]
    [SerializeField] private float _noiseScale = 0.02f;
    [SerializeField] private float _baseHeight = 80f;
    [SerializeField] private float _amplitude = 40f;

    // (cx,cz) -> 데이터/청크 인스턴스
    private readonly Dictionary<Vector2Int, ChunkColumnData> _columns = new Dictionary<Vector2Int, ChunkColumnData>();
    private readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();

    private readonly List<Vector3> _verts = new List<Vector3>(8192);
    private readonly List<int> _tris = new List<int>(8192);

    // 편집 시 더티 서브청크(중복 제거용)
    private readonly HashSet<long> _dirtySubChunks = new HashSet<long>();

    private void Start()
    {
        GenerateAndMesh();
    }

    [ContextMenu("Generate And Mesh")]
    public void GenerateAndMesh()
    {
        if (_chunkPrefab == null) return;

        // 기존 자식(생성된 청크) 제거
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        _columns.Clear();
        _chunks.Clear();
        _dirtySubChunks.Clear();

        var generator = new HeightmapDensityGenerator(
            noiseScale: _noiseScale,
            baseHeight: _baseHeight,
            amplitude: _amplitude,
            solidAmount: 128,
            airAmount: 0
        );

        for (int cz = 0; cz < _countZ; cz++)
        {
            for (int cx = 0; cx < _countX; cx++)
            {
                // Chunk 생성
                Chunk chunk = Instantiate(_chunkPrefab, transform);
                chunk.name = $"Chunk_{cx}_{cz}";
                chunk.SetChunkCoord(cx, cz);

                var key = new Vector2Int(cx, cz);
                _chunks[key] = chunk;

                // 데이터 생성
                var data = new ChunkColumnData();
                generator.FillColumn(cx, cz, data);
                _columns[key] = data;

                // 각 서브청크 메싱 후 적용
                for (int sy = 0; sy < Chunk.SubChunkCount; sy++)
                {
                    RebuildSubChunkInternal(cx, cz, sy);
                }
            }
        }
    }

    public bool TryGetColumnData(int chunkX, int chunkZ, out ChunkColumnData data)
    {
        return _columns.TryGetValue(new Vector2Int(chunkX, chunkZ), out data);
    }

    public bool TryGetChunk(int chunkX, int chunkZ, out Chunk chunk)
    {
        return _chunks.TryGetValue(new Vector2Int(chunkX, chunkZ), out chunk);
    }

    /// <summary>
    /// 구 브러시. deltaAmount가 음수면 파괴, 양수면 생성.
    /// worldPos는 월드 좌표(1유닛=1복셀).
    /// </summary>
    // 교체 범위: WorldBootstrap10x10.cs 의 ApplyBallBrush(Vector3 worldPos, float radius, int deltaAmount) 함수 전체

    public void ApplyBallBrush(Vector3 worldPos, float radius, int deltaAmount)
    {
        if (radius <= 0f || deltaAmount == 0) return;

        // 코너 샘플이 정수 좌표이므로 중심을 정수로 스냅(체감 개선)
        int cx = Mathf.RoundToInt(worldPos.x);
        int cy = Mathf.RoundToInt(worldPos.y);
        int cz = Mathf.RoundToInt(worldPos.z);

        int rInt = Mathf.CeilToInt(radius);

        int minX = cx - rInt;
        int maxX = cx + rInt;
        int minY = cy - rInt;
        int maxY = cy + rInt;
        int minZ = cz - rInt;
        int maxZ = cz + rInt;

        // 코너 샘플 Y 범위(0..256)
        if (maxY < 0 || minY > 256) return;
        if (minY < 0) minY = 0;
        if (maxY > 256) maxY = 256;

        _dirtySubChunks.Clear();

        float r = radius;
        float invR = 1f / Mathf.Max(0.0001f, r);
        const float epsilon = 0.25f; // dist=0 근처 폭발 방지

        for (int wz = minZ; wz <= maxZ; wz++)
        {
            for (int wy = minY; wy <= maxY; wy++)
            {
                for (int wx = minX; wx <= maxX; wx++)
                {
                    Vector3 sp = new Vector3(wx, wy, wz);
                    float dist = Vector3.Distance(sp, worldPos);
                    if (dist > r) continue;

                    // 가까울수록 강하게 (0..1)
                    float t = 1f - (dist * invR);

                    // 중심부 강화(zip 느낌)
                    float boost = 1f / Mathf.Max(dist, epsilon);

                    int d = Mathf.RoundToInt(deltaAmount * t * boost);

                    // 최소 변화 보장(안 그러면 "안 깎이는" 구간이 생김)
                    if (d == 0) d = (deltaAmount > 0) ? 1 : -1;

                    ApplyDeltaToSample(wx, wy, wz, d);
                }
            }
        }

        RebuildDirtySubChunks();
    }

    /// <summary>
    /// 월드 코너 샘플 1개(wx,wy,wz)에 deltaAmount를 적용한다.
    /// 경계 샘플(wx가 16의 배수 등)은 이웃 컬럼에도 같이 반영한다.
    /// </summary>
    private void ApplyDeltaToSample(int wx, int wy, int wz, int deltaAmount)
    {
        // 범위 밖은 무시(현재 월드는 0..(count*16)만 생성한다고 가정)
        // 음수 좌표까지 지원할 계획이면 여기 로직 확장 필요.
        if (wy < 0 || wy > 256) return;

        // 이 샘플이 포함될 수 있는 컬럼 후보들(경계 공유)
        GetColumnCandidatesForSample(wx, out int cx0, out int cx1, out bool hasCx1);
        GetColumnCandidatesForSample(wz, out int cz0, out int cz1, out bool hasCz1);

        ApplyDeltaToColumnSample(cx0, cz0, wx, wy, wz, deltaAmount);
        if (hasCx1) ApplyDeltaToColumnSample(cx1, cz0, wx, wy, wz, deltaAmount);
        if (hasCz1) ApplyDeltaToColumnSample(cx0, cz1, wx, wy, wz, deltaAmount);
        if (hasCx1 && hasCz1) ApplyDeltaToColumnSample(cx1, cz1, wx, wy, wz, deltaAmount);
    }

    private void ApplyDeltaToColumnSample(int cx, int cz, int wx, int wy, int wz, int deltaAmount)
    {
        var key = new Vector2Int(cx, cz);
        if (!_columns.TryGetValue(key, out var data))
            return;

        int localX = wx - cx * Chunk.SizeX; // 0..16
        int localZ = wz - cz * Chunk.SizeZ; // 0..16

        if ((uint)localX > 16u || (uint)localZ > 16u) return; // 샘플 범위 밖

        byte oldV = data.Get(localX, wy, localZ);
        int nv = oldV + deltaAmount;
        if (nv < 0) nv = 0;
        else if (nv > 128) nv = 128;
        byte newV = (byte)nv;

        if (newV == oldV) return;

        data.Set(localX, wy, localZ, newV);

        // 영향 서브청크 마킹(안전하게 y층 위/아래 둘 다)
        int syA = Mathf.Clamp(wy / Chunk.SubSizeY, 0, 15);
        int syB = Mathf.Clamp((wy - 1) / Chunk.SubSizeY, 0, 15);

        MarkDirty(cx, cz, syA);
        MarkDirty(cx, cz, syB);
    }

    private void MarkDirty(int cx, int cz, int sy)
    {
        long k = PackDirtyKey(cx, cz, sy);
        _dirtySubChunks.Add(k);
    }

    private void RebuildDirtySubChunks()
    {
        foreach (long k in _dirtySubChunks)
        {
            UnpackDirtyKey(k, out int cx, out int cz, out int sy);
            RebuildSubChunkInternal(cx, cz, sy);
        }
    }

    private void RebuildSubChunkInternal(int cx, int cz, int sy)
    {
        var key = new Vector2Int(cx, cz);
        if (!_chunks.TryGetValue(key, out var chunk)) return;
        if (!_columns.TryGetValue(key, out var data)) return;

        Transform root = chunk.GetSubChunkRoot(sy);
        if (root == null) return;

        SubChunk sub = root.GetComponent<SubChunk>();
        if (sub == null) return;

        sub.SetMaterial(_terrainMaterial);

        MarchingCubesMesher.BuildSubChunkMesh(data, sy, _verts, _tris);

        if (_verts.Count > 0)
        {
            int y0 = sy * Chunk.SubSizeY;
            for (int i = 0; i < _verts.Count; i++)
            {
                var v = _verts[i];
                v.y -= y0;
                _verts[i] = v;
            }

            sub.ApplyMesh(_verts.ToArray(), _tris.ToArray(), null);
        }
        else
        {
            sub.ApplyMesh(System.Array.Empty<Vector3>(), System.Array.Empty<int>(), null);
        }
    }

    // wx가 16의 배수면 좌/우 컬럼이 공유하는 코너 샘플이다.
    // base는 floor(wx/16). 경계면이면 base와 base-1 두 컬럼 후보.
    private static void GetColumnCandidatesForSample(int wCoord, out int baseChunk, out int secondChunk, out bool hasSecond)
    {
        baseChunk = Mathf.FloorToInt(wCoord / (float)Chunk.SizeX);
        hasSecond = (wCoord % Chunk.SizeX) == 0;
        secondChunk = baseChunk - 1;
    }

    private static long PackDirtyKey(int cx, int cz, int sy)
    {
        // 간단 패킹(부호 포함). 현재 월드가 양수 좌표만 쓰는 전제라 충분.
        // cx/cz가 커질 가능성 있으면 bit 폭을 늘리면 됨.
        long a = (long)(cx & 0x1FFFFF); // 21bit
        long b = (long)(cz & 0x1FFFFF); // 21bit
        long c = (long)(sy & 0x1F);     // 5bit
        return (a << 26) | (b << 5) | c;
    }

    private static void UnpackDirtyKey(long k, out int cx, out int cz, out int sy)
    {
        sy = (int)(k & 0x1F);
        cz = (int)((k >> 5) & 0x1FFFFF);
        cx = (int)((k >> 26) & 0x1FFFFF);
    }
}