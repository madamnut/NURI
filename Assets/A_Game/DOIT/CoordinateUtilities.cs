using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // - 복셀 크기 = 1 유닛
    // - Chunk: 16 x 16 x 256 (로딩/저장/데이터 단위, (cx, cz))
    // - SubChunk: 16 x 16 x 16 (메시/콜라이더 단위, (cx, cz, sy)), sy = 0..15
    //
    // 주의:
    // - 음수 좌표에서 / 연산은 0 쪽으로 절삭(truncation)되므로 경계가 깨질 수 있다.
    //   → floorDiv(바닥 나눗셈)를 사용한다.
    public static class CoordinateUtilities
    {
        // 월드 위치(Vector3) -> 복셀 좌표(int3)
        // 경계 튐을 줄이기 위해 round가 아니라 floor를 사용한다.
        public static int3 WorldToVoxelPosition(in Vector3 worldPosition)
        {
            return new int3(
                (int)math.floor(worldPosition.x),
                (int)math.floor(worldPosition.y),
                (int)math.floor(worldPosition.z)
            );
        }

        // 복셀 좌표 -> Chunk 좌표(cx, cz)
        public static int2 VoxelToChunkCoordinate(in int3 voxelPosition)
        {
            int cx = FloorDiv(voxelPosition.x, WorldSettings.ChunkSize.x);
            int cz = FloorDiv(voxelPosition.z, WorldSettings.ChunkSize.z);
            return new int2(cx, cz);
        }

        // 복셀 좌표 -> Chunk 로컬 좌표(lx, lz) (0..15)
        public static int2 VoxelToChunkLocalXZ(in int3 voxelPosition)
        {
            int2 c = VoxelToChunkCoordinate(voxelPosition);

            int lx = voxelPosition.x - (c.x * WorldSettings.ChunkSize.x);
            int lz = voxelPosition.z - (c.y * WorldSettings.ChunkSize.z);

            return new int2(lx, lz);
        }

        // 복셀 Y -> SubChunk 인덱스(sy) (0..15)
        // 전 높이 256(0..255)을 전부 생성/저장한다는 전제에서 clamp를 적용한다.
        public static int VoxelYToSubChunkIndex(int voxelY)
        {
            int sy = FloorDiv(voxelY, WorldSettings.SubChunkSize.y);
            return math.clamp(sy, 0, (WorldSettings.ChunkSize.y / WorldSettings.SubChunkSize.y) - 1);
        }

        // 복셀 Y -> SubChunk 로컬 Y (ly) (0..15)
        public static int VoxelYToSubChunkLocalY(int voxelY)
        {
            int sy = VoxelYToSubChunkIndex(voxelY);
            return voxelY - (sy * WorldSettings.SubChunkSize.y);
        }

        // Chunk 좌표(cx,cz) + Chunk 로컬(lx,lz) -> 복셀 XZ
        public static int2 ChunkToVoxelXZ(in int2 chunkCoordinate, in int2 chunkLocalXZ)
        {
            int vx = (chunkCoordinate.x * WorldSettings.ChunkSize.x) + chunkLocalXZ.x;
            int vz = (chunkCoordinate.y * WorldSettings.ChunkSize.z) + chunkLocalXZ.y;
            return new int2(vx, vz);
        }

        // Chunk 좌표(cx,cz) + SubChunk 인덱스(sy) + 로컬(lx,ly,lz) -> 복셀 좌표
        public static int3 ToVoxelPosition(in int2 chunkCoordinate, int subChunkIndex, in int3 subChunkLocal)
        {
            int vx = (chunkCoordinate.x * WorldSettings.ChunkSize.x) + subChunkLocal.x;
            int vy = (subChunkIndex * WorldSettings.SubChunkSize.y) + subChunkLocal.y;
            int vz = (chunkCoordinate.y * WorldSettings.ChunkSize.z) + subChunkLocal.z;
            return new int3(vx, vy, vz);
        }

        // 바닥 나눗셈: a/b (b>0)에서 음수도 올바르게 "바닥"으로 떨어지도록 처리한다.
        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            int r = a % b;

            if (r != 0 && a < 0)
            {
                q -= 1;
            }

            return q;
        }
    }
}