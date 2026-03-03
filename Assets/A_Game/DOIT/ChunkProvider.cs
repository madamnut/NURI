using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // Chunk/서브청크 런타임 오브젝트를 생성하고 ChunkStore에 등록한다.
    //
    // - Chunk는 (cx, cz)로 식별되는 16 x 16 x 256 단위
    // - SubChunk는 (cx, cz, sy)로 식별되는 16 x 16 x 16 단위 (sy=0..15)
    // - Chunk 루트는 월드 좌표 (cx*16, 0, cz*16)에 배치한다.
    // - SubChunk는 Chunk 루트의 자식이며 로컬 좌표 (0, sy*16, 0)에 배치한다.
    public sealed class ChunkProvider : MonoBehaviour
    {
        [Header("설정/참조")]
        [SerializeField] private WorldSettings _worldSettings;
        [SerializeField] private ChunkStore _chunkStore;

        public WorldSettings WorldSettings => _worldSettings;
        public ChunkStore ChunkStore => _chunkStore;

        public bool EnsureChunkExistsAtCoordinate(int2 chunkCoordinate, out ChunkProperties chunk)
        {
            if (_chunkStore != null && _chunkStore.TryGetChunk(chunkCoordinate, out chunk) && chunk != null)
            {
                return true;
            }

            chunk = CreateUnloadedChunkToCoordinate(chunkCoordinate);
            return chunk != null;
        }

        // Chunk 오브젝트와 16개의 SubChunk 오브젝트를 생성해 스토어에 등록한다.
        // "Unloaded"의 의미: 메시/콜라이더는 아직 생성되지 않았을 수 있다.
        public ChunkProperties CreateUnloadedChunkToCoordinate(int2 chunkCoordinate)
        {
            if (_worldSettings == null)
            {
                Debug.LogError("WorldSettings가 할당되지 않았습니다.");
                return null;
            }

            if (_chunkStore == null)
            {
                Debug.LogError("ChunkStore가 할당되지 않았습니다.");
                return null;
            }

            if (_worldSettings.ChunkPrefab == null)
            {
                Debug.LogError("ChunkPrefab이 할당되지 않았습니다.");
                return null;
            }

            if (_worldSettings.SubChunkPrefab == null)
            {
                Debug.LogError("SubChunkPrefab이 할당되지 않았습니다.");
                return null;
            }

            Vector3 chunkWorldPos = new Vector3(
                chunkCoordinate.x * WorldSettings.ChunkSize.x,
                0f,
                chunkCoordinate.y * WorldSettings.ChunkSize.z
            );

            GameObject chunkGo = Instantiate(_worldSettings.ChunkPrefab, chunkWorldPos, Quaternion.identity, transform);
            chunkGo.name = $"Chunk ({chunkCoordinate.x}, {chunkCoordinate.y})";

            ChunkProperties chunkProps = chunkGo.GetComponent<ChunkProperties>();
            if (chunkProps == null)
            {
                chunkProps = chunkGo.AddComponent<ChunkProperties>();
            }

            int subChunkCount = WorldSettings.ChunkSize.y / WorldSettings.SubChunkSize.y; // 256/16 = 16
            var subChunks = new ChunkProperties.SubChunk[subChunkCount];

            for (int sy = 0; sy < subChunkCount; sy++)
            {
                GameObject subGo = Instantiate(_worldSettings.SubChunkPrefab, chunkGo.transform);
                subGo.name = $"SubChunk sy={sy}";
                subGo.transform.localPosition = new Vector3(0f, sy * WorldSettings.SubChunkSize.y, 0f);
                subGo.transform.localRotation = Quaternion.identity;
                subGo.transform.localScale = Vector3.one;

                var sc = new ChunkProperties.SubChunk();
                sc.Initialize(sy, subGo);
                sc.IsDirty = false;
                sc.IsMeshGenerated = false;

                subChunks[sy] = sc;
            }

            chunkProps.Initialize(chunkCoordinate, subChunks);
            _chunkStore.AddChunk(chunkProps);

            return chunkProps;
        }
    }
}