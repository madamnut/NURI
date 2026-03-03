using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // Dirty 상태인 SubChunk의 메시/콜라이더를 갱신한다.
    //
    // 규칙:
    // - 데이터(밀도/색)는 VoxelDataStore / VoxelColorStore가 Chunk 단위로 관리한다.
    // - 렌더링/충돌은 SubChunk 단위로만 갱신한다.
    //
    // 주의:
    // - 실제 메시 생성(마칭큐브 + 잡)은 별도 Mesher 컴포넌트가 담당한다.
    public sealed class ChunkUpdater : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ChunkStore _chunkStore;
        [SerializeField] private VoxelDataStore _voxelDataStore;
        [SerializeField] private VoxelColorStore _voxelColorStore;
        [SerializeField] private VoxelMesher _voxelMesher;

        [Header("성능")]
        [SerializeField] private int _subChunkUpdateRate = 2;

        private void Update()
        {
            if (_chunkStore == null || _voxelDataStore == null || _voxelMesher == null)
            {
                return;
            }

            int remaining = math.max(0, _subChunkUpdateRate);

            foreach (ChunkProperties chunk in _chunkStore.GetAllChunks())
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (chunk == null || !chunk.IsInitialized)
                {
                    continue;
                }

                ChunkProperties.SubChunk[] subChunks = chunk.SubChunks;
                if (subChunks == null || subChunks.Length == 0)
                {
                    continue;
                }

                for (int sy = 0; sy < subChunks.Length; sy++)
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    ChunkProperties.SubChunk sc = subChunks[sy];
                    if (sc == null || !sc.IsDirty)
                    {
                        continue;
                    }

                    GenerateSubChunkMeshImmediate(chunk.ChunkCoordinate, sc);

                    sc.IsDirty = false;
                    remaining--;
                }
            }
        }

        private void GenerateSubChunkMeshImmediate(int2 chunkCoordinate, ChunkProperties.SubChunk subChunk)
        {
            if (subChunk == null || subChunk.MeshFilter == null)
            {
                return;
            }

            // Mesh 인스턴스 재사용 (없으면 생성)
            Mesh mesh = subChunk.MeshFilter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = $"SubChunkMesh ({chunkCoordinate.x},{chunkCoordinate.y}) sy={subChunk.Sy}";
                subChunk.MeshFilter.sharedMesh = mesh;
            }
            else
            {
                mesh.Clear();
            }

            // Mesher가 마칭큐브(잡 포함)로 mesh를 채운다.
            // - color store는 선택(없어도 동작 가능)이라 null 허용
            _voxelMesher.GenerateSubChunkMeshImmediate(
                _voxelDataStore,
                _voxelColorStore,
                chunkCoordinate,
                subChunk.Sy,
                mesh
            );

            subChunk.IsMeshGenerated = true;

            // 콜라이더 갱신 (MeshCollider는 sharedMesh 갱신이 즉시 반영되지 않는 케이스가 있어 토글로 강제)
            if (subChunk.MeshCollider != null)
            {
                subChunk.MeshCollider.enabled = false;
                subChunk.MeshCollider.sharedMesh = null;
                subChunk.MeshCollider.sharedMesh = mesh;
                subChunk.MeshCollider.enabled = true;
            }
        }
    }
}