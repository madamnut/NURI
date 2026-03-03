using UnityEngine;

public sealed class Chunk : MonoBehaviour
{
    public const int SizeX = 16;
    public const int SizeZ = 16;
    public const int SizeY = 256;

    // 서브청크 크기(복셀/셀 기준)
    public const int SubSizeX = 16;
    public const int SubSizeZ = 16;
    public const int SubSizeY = 16;

    // 서브청크 개수(수직 16개 고정)
    public const int SubChunkCount = 16;

    [Header("청크 좌표 (X, Z)")]
    [SerializeField] private int _chunkX;
    [SerializeField] private int _chunkZ;

    [Header("서브청크 루트들 (직접 할당, 길이 16)")]
    [SerializeField] private Transform[] _subChunkRoots = new Transform[SubChunkCount];

    public int ChunkX => _chunkX;
    public int ChunkZ => _chunkZ;

    // 월드 원점(1 유닛 = 1 복셀/셀 가정)
    public Vector3 WorldOrigin => new Vector3(_chunkX * SizeX, 0f, _chunkZ * SizeZ);

    public Transform GetSubChunkRoot(int index)
    {
        if ((uint)index >= (uint)SubChunkCount) return null;
        return _subChunkRoots[index];
    }

    // 서브청크 루트 배열(인스펙터용) 그대로 노출
    public Transform[] SubChunkRoots => _subChunkRoots;

    // 청크 좌표를 설정하고, 청크 루트 위치를 그에 맞춰 이동
    public void SetChunkCoord(int chunkX, int chunkZ)
    {
        _chunkX = chunkX;
        _chunkZ = chunkZ;
        transform.position = WorldOrigin;
    }

    private void OnValidate()
    {
        // 자동 생성/자동 할당은 하지 않음. 배열 길이만 16으로 유지.
        if (_subChunkRoots == null || _subChunkRoots.Length != SubChunkCount)
            _subChunkRoots = new Transform[SubChunkCount];
    }
}