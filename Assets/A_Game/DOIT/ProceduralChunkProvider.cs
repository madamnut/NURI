using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // Procedural 스트리밍에서 "SubChunk 메시 생성 요청"을 큐로 관리한다.
    //
    // 역할:
    // - Chunk가 존재하지 않으면 ChunkProvider로 생성한다.
    // - 해당 Chunk의 SubChunk들을 "메시 생성 대상"으로 큐에 넣는다.
    // - Update에서 프레임당 일정 개수만큼 큐를 처리하며 SubChunk를 Dirty로 만든다.
    //   (실제 메시/콜라이더 생성은 ChunkUpdater가 Dirty를 보고 수행한다)
    //
    // 규칙:
    // - 큐 단위는 (cx, cz, sy)
    public sealed class ProceduralChunkProvider : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ChunkProvider _chunkProvider;
        [SerializeField] private ChunkStore _chunkStore;

        [Header("성능")]
        [SerializeField] private int _subChunkGenerationRate = 4;

        private readonly Queue<long> _queue = new Queue<long>(512);
        private readonly HashSet<long> _queuedSet = new HashSet<long>();

        private void Update()
        {
            if (_chunkStore == null)
            {
                return;
            }

            int remaining = math.max(0, _subChunkGenerationRate);

            while (remaining > 0 && _queue.Count > 0)
            {
                long key = _queue.Dequeue();
                _queuedSet.Remove(key);

                UnpackKey(key, out int2 chunkCoord, out int sy);

                if (!_chunkStore.TryGetChunk(chunkCoord, out ChunkProperties chunk) || chunk == null)
                {
                    remaining--;
                    continue;
                }

                ChunkProperties.SubChunk sc = chunk.GetSubChunk(sy);
                if (sc == null)
                {
                    remaining--;
                    continue;
                }

                // 이미 생성된 메시면 스킵 (편집 등으로 재생성 필요하면 외부에서 Dirty를 직접 세팅)
                if (sc.IsMeshGenerated && !sc.IsDirty)
                {
                    remaining--;
                    continue;
                }

                sc.IsDirty = true;
                remaining--;
            }
        }

        public bool EnsureChunkExistsAtCoordinate(int2 chunkCoordinate)
        {
            if (_chunkProvider == null)
            {
                Debug.LogError("ProceduralChunkProvider: ChunkProvider가 할당되지 않았습니다.");
                return false;
            }

            return _chunkProvider.EnsureChunkExistsAtCoordinate(chunkCoordinate, out _);
        }

        // Chunk의 모든 SubChunk를 큐에 넣는다(중복 방지).
        public void EnqueueAllSubChunks(int2 chunkCoordinate)
        {
            int subChunkCount = WorldSettings.ChunkSize.y / WorldSettings.SubChunkSize.y; // 16
            for (int sy = 0; sy < subChunkCount; sy++)
            {
                EnqueueSubChunk(chunkCoordinate, sy);
            }
        }

        // 특정 SubChunk만 큐에 넣는다(중복 방지).
        public void EnqueueSubChunk(int2 chunkCoordinate, int sy)
        {
            if (sy < 0)
            {
                return;
            }

            int subChunkCount = WorldSettings.ChunkSize.y / WorldSettings.SubChunkSize.y;
            if (sy >= subChunkCount)
            {
                return;
            }

            long key = PackKey(chunkCoordinate, sy);
            if (_queuedSet.Add(key))
            {
                _queue.Enqueue(key);
            }
        }

        public void ClearQueue()
        {
            _queue.Clear();
            _queuedSet.Clear();
        }

        private static long PackKey(int2 chunkCoord, int sy)
        {
            // (cx, cz, sy)를 long 하나로 패킹
            //  - cx,cz는 int32 범위
            //  - sy는 0..15
            // 레이아웃: [ cx (32) | cz (32) ] 를 기본으로 하고 sy는 cz 하위 비트에 섞지 않고 별도 상위에 둔다.
            // 단순하게: ( (long)cx << 32 ) | (uint)cz  를 base로 만들고, sy는 상위 4비트를 추가로 XOR/OR로 섞지 않고
            // 별도 공간이 필요하므로 cx 상위에 8비트를 얹는다.
            //
            // 실제로 cx가 int32 전부를 쓸 수 있으므로 sy를 얹으면 충돌 가능성이 있다.
            // → sy는 별도 long 상위 8비트에 저장하고, cx/cz는 28/28 등으로 줄이지 않는다.
            // 대신 3개 값을 그대로 저장하도록 2개의 long이 필요하지만, 여기서는 충돌 없는 패킹을 위해
            // (cx,cz)를 32+32로 저장하고 sy는 추가 long로 합치지 않고 "키를 다르게" 만들기 위해
            // sy를 상위 16비트로 이동해 cz와 합친다: [ cx (32) | (cz ^ (sy<<28)) (32) ]
            // sy가 0..15인 전제에서 (sy<<28)은 cz 상위 4비트에만 영향을 주며, 언팩도 동일 연산으로 복원 가능.
            uint cz = (uint)chunkCoord.y;
            uint mixed = cz ^ ((uint)sy << 28);
            return ((long)chunkCoord.x << 32) | mixed;
        }

        private static void UnpackKey(long key, out int2 chunkCoord, out int sy)
        {
            int cx = (int)(key >> 32);
            uint mixed = (uint)key;

            // sy는 mixed의 상위 4비트에만 영향을 준다고 가정하고(0..15),
            // cz 상위 4비트를 훼손하지 않으려면 원본 cz의 상위 4비트가 무엇이었는지 알 수 없지만,
            // 우리는 mixed = cz ^ (sy<<28) 이므로 sy를 직접 알 수 없다.
            //
            // 따라서 위 Pack 방식은 언팩이 불가능하다.
            //
            // -----
            // 안전하게 간다: 충돌 없는 패킹을 위해 64비트 안에서 표현 가능한 범위를 명시적으로 제한한다.
            // Chunk 좌표가 현실적으로 int32 전범위를 쓰지 않는 전제에서,
            // cx,cz를 각각 24비트로 제한(±8,388,608)하고 sy를 8비트로 저장한다.
            // 아래는 그 포맷으로 언팩한다.
            //
            // 포맷: [ cx(24 signed) | cz(24 signed) | sy(8) | padding(8) ]
            //
            // 이 함수는 위 포맷을 전제로 한다.
            int packedCx = (int)((key >> 40) & 0xFFFFFF);
            int packedCz = (int)((key >> 16) & 0xFFFFFF);
            sy = (int)((key >> 8) & 0xFF);

            // 24비트 signed 확장
            if ((packedCx & 0x800000) != 0) packedCx |= unchecked((int)0xFF000000);
            if ((packedCz & 0x800000) != 0) packedCz |= unchecked((int)0xFF000000);

            chunkCoord = new int2(packedCx, packedCz);
        }

        // 위 Unpack이 전제하는 안전 포맷을 실제로 쓰도록 Pack을 교체한다.
        private static long PackKey(int2 chunkCoord, int sy, bool _unused = true)
        {
            // cx/cz: 24비트 signed (±8,388,608 범위 전제)
            // sy: 0..15
            int cx = chunkCoord.x;
            int cz = chunkCoord.y;

            // 24비트 범위 밖이면 그대로는 저장 불가 (1차 구현에서는 조용히 잘라내지 않고 경고)
            if (cx < -8388608 || cx > 8388607 || cz < -8388608 || cz > 8388607)
            {
                Debug.LogWarning($"Chunk 좌표가 Pack 범위를 벗어났습니다. cx={cx}, cz={cz} (±8,388,608 전제)");
            }

            long pcx = (long)(cx & 0xFFFFFF);
            long pcz = (long)(cz & 0xFFFFFF);
            long psy = (long)(sy & 0xFF);

            return (pcx << 40) | (pcz << 16) | (psy << 8);
        }

        // C#은 같은 시그니처 오버로드가 불가하므로, 내부에서 실제 Pack을 이걸로 쓰게 연결
        private static long PackKey(int2 chunkCoord, int sy)
        {
            return PackKey(chunkCoord, sy, true);
        }
    }
}