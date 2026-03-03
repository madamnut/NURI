// Assets/Scripts/World/WorldBootstrap10x10.cs
// 교체 범위: 파일 전체 (이전 생성/데이터 생성 + 메싱/적용까지 포함)

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

    // (cx,cz) -> 데이터
    private readonly Dictionary<Vector2Int, ChunkColumnData> _columns = new Dictionary<Vector2Int, ChunkColumnData>();

    private readonly List<Vector3> _verts = new List<Vector3>(8192);
    private readonly List<int> _tris = new List<int>(8192);

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

                // 데이터 생성
                var key = new Vector2Int(cx, cz);
                var data = new ChunkColumnData();
                generator.FillColumn(cx, cz, data);
                _columns[key] = data;

                // 각 서브청크 메싱 후 적용
                for (int sy = 0; sy < Chunk.SubChunkCount; sy++)
                {
                    Transform root = chunk.GetSubChunkRoot(sy);
                    if (root == null) continue;

                    SubChunk sub = root.GetComponent<SubChunk>();
                    if (sub == null) continue;

                    sub.SetMaterial(_terrainMaterial);

                    MarchingCubesMesher.BuildSubChunkMesh(data, sy, _verts, _tris);

                    // 서브청크는 Chunk 로컬 기준(자기 localPosition이 이미 y=sy*16)
                    // 메셔는 (0..16, y0..y0+16, 0..16) 좌표로 만들었으므로
                    // SubChunk 로컬로 맞추려면 y를 subIndex*16만큼 빼야 함.
                    // (x,z는 0..16 그대로)
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
            }
        }
    }

    public bool TryGetColumnData(int chunkX, int chunkZ, out ChunkColumnData data)
    {
        return _columns.TryGetValue(new Vector2Int(chunkX, chunkZ), out data);
    }
}