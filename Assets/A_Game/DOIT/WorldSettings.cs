using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // 월드 생성/표현에 필요한 설정 값 모음.
    //
    // 용어(고정):
    // - Chunk: 16 x 16 x 256 (로딩/저장/데이터 단위, (cx, cz)로 식별)
    // - SubChunk: 16 x 16 x 16 (메시/콜라이더 단위, (cx, cz, sy)로 식별)
    // - 복셀 크기 = 1 유닛
    // - 메시 생성은 마칭 큐브 기반
    [System.Serializable]
    public sealed class WorldSettings
    {
        // int3는 const가 불가능하므로 static readonly로 고정한다.
        public static readonly int3 ChunkSize = new int3(16, 256, 16);
        public static readonly int3 SubChunkSize = new int3(16, 16, 16);

        [Header("프리팹")]
        [SerializeField] private GameObject _chunkPrefab;
        [SerializeField] private GameObject _subChunkPrefab;

        public GameObject ChunkPrefab => _chunkPrefab;
        public GameObject SubChunkPrefab => _subChunkPrefab;
    }
}