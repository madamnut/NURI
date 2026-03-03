using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

namespace NURI
{
    // Procedural 스트리밍(Chunk 단위) 담당.
    //
    // 규칙:
    // - Chunk: (cx, cz) 16 x 16 x 256 (로딩/저장/데이터 단위)
    // - SubChunk: (cx, cz, sy) 16 x 16 x 16 (메시/콜라이더 단위)
    //
    // 동작:
    // - renderDistance: 화면에 보이는 Chunk 반경(정사각)
    // - loadingBufferSize: 데이터만 미리 준비하는 추가 반경
    // - 이동 시 범위 밖으로 나간 데이터/Chunk 오브젝트를 새 좌표로 Move(재사용)한 뒤
    //   새 좌표에 대해 데이터 재생성 + SubChunk Dirty 마킹을 수행한다.
    //
    // 참고:
    // - 1차 구현 편의상, 이 스크립트 내부에 간단한 "지형 생성(밀도/색)"을 포함한다.
    //   (나중에 ProceduralVoxelDataGenerator 같은 전용 모듈로 분리해도 됨)
    public sealed class ProceduralWorldGenerator : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private Transform _player;
        [SerializeField] private ChunkProvider _chunkProvider;
        [SerializeField] private ChunkStore _chunkStore;
        [SerializeField] private VoxelDataStore _voxelDataStore;
        [SerializeField] private VoxelColorStore _voxelColorStore;

        [Header("스트리밍 범위(Chunk 단위)")]
        [SerializeField] private int _renderDistance = 6;
        [SerializeField] private int _loadingBufferSize = 2;

        [Header("간이 지형 생성 파라미터(1차용)")]
        [SerializeField] private int _baseHeight = 64;        // 0..255
        [SerializeField] private int _heightVariation = 24;   // 0..255
        [SerializeField] private float _noiseScale = 0.02f;

        private int2 _currentCenter;
        private bool _hasCenter;

        private readonly List<int2> _tmpOutside = new List<int2>(256);
        private readonly List<int2> _tmpNeeded = new List<int2>(256);

        private void Start()
        {
            if (_player == null || _chunkProvider == null || _chunkStore == null || _voxelDataStore == null)
            {
                Debug.LogError("ProceduralWorldGenerator: 필수 참조가 누락되었습니다.");
                enabled = false;
                return;
            }

            _currentCenter = GetPlayerChunkCoordinate();
            _hasCenter = true;

            GenerateTerrainAroundCenter(_currentCenter);
        }

        private void Update()
        {
            if (!_hasCenter)
            {
                return;
            }

            int2 newCenter = GetPlayerChunkCoordinate();
            if (newCenter.x == _currentCenter.x && newCenter.y == _currentCenter.y)
            {
                return;
            }

            UpdateTerrainForMove(_currentCenter, newCenter);
            _currentCenter = newCenter;
        }

        private int2 GetPlayerChunkCoordinate()
        {
            int3 voxelPos = CoordinateUtilities.WorldToVoxelPosition(_player.position);
            return CoordinateUtilities.VoxelToChunkCoordinate(voxelPos);
        }

        private void GenerateTerrainAroundCenter(int2 center)
        {
            int dataRange = math.max(0, _renderDistance + _loadingBufferSize);

            // 1) 데이터만 미리 준비(버퍼 포함)
            FillNeededCoordinates(center, dataRange, _tmpNeeded);
            for (int i = 0; i < _tmpNeeded.Count; i++)
            {
                int2 coord = _tmpNeeded[i];

                EnsureAndGenerateChunkData(coord);
                EnsureAndGenerateChunkColor(coord);
            }

            // 2) 렌더 범위 Chunk 오브젝트 확보 + Dirty 마킹
            FillNeededCoordinates(center, _renderDistance, _tmpNeeded);
            for (int i = 0; i < _tmpNeeded.Count; i++)
            {
                int2 coord = _tmpNeeded[i];
                EnsureChunkObject(coord);
                MarkAllSubChunksDirty(coord);
            }
        }

        private void UpdateTerrainForMove(int2 oldCenter, int2 newCenter)
        {
            int dataRange = math.max(0, _renderDistance + _loadingBufferSize);

            // ---------- 데이터 Move(버퍼 포함) ----------
            // 밖으로 나간 좌표들(재사용 가능한 source)과 새로 필요한 좌표들(target)을 만든다.
            BuildOutsideCoordinates(oldCenter, dataRange, _tmpOutside);
            BuildNewlyNeededCoordinates(newCenter, dataRange, _tmpNeeded);

            int moveCount = math.min(_tmpOutside.Count, _tmpNeeded.Count);
            for (int i = 0; i < moveCount; i++)
            {
                int2 source = _tmpOutside[i];
                int2 target = _tmpNeeded[i];

                _voxelDataStore.MoveChunkData(source, target);
                EnsureAndGenerateChunkData(target);

                if (_voxelColorStore != null)
                {
                    _voxelColorStore.MoveChunkColorData(source, target);
                    EnsureAndGenerateChunkColor(target);
                }
            }

            // moveCount 이후로 남은 newly-needed는 새로 생성/생성
            for (int i = moveCount; i < _tmpNeeded.Count; i++)
            {
                int2 target = _tmpNeeded[i];
                EnsureAndGenerateChunkData(target);
                EnsureAndGenerateChunkColor(target);
            }

            // ---------- Chunk 오브젝트 Move(렌더 범위) ----------
            // 렌더 범위 밖 chunk들을 새 렌더 범위의 결손 좌표로 이동시킨다.
            _tmpOutside.Clear();
            _tmpOutside.AddRange(_chunkStore.GetChunkCoordinatesOutsideOfRange(newCenter, _renderDistance));

            BuildNewlyNeededChunkObjects(newCenter, _renderDistance, _tmpNeeded);

            int chunkMoveCount = math.min(_tmpOutside.Count, _tmpNeeded.Count);
            for (int i = 0; i < chunkMoveCount; i++)
            {
                int2 source = _tmpOutside[i];
                int2 target = _tmpNeeded[i];

                _chunkStore.MoveChunk(source, target);
                // MoveChunk 내부에서 SubChunk Dirty/IsMeshGenerated 초기화까지 처리함.
            }

            // 부족분은 새로 생성
            for (int i = chunkMoveCount; i < _tmpNeeded.Count; i++)
            {
                int2 coord = _tmpNeeded[i];
                EnsureChunkObject(coord);
                MarkAllSubChunksDirty(coord);
            }
        }

        private void EnsureChunkObject(int2 chunkCoordinate)
        {
            _chunkProvider.EnsureChunkExistsAtCoordinate(chunkCoordinate, out _);
        }

        private void MarkAllSubChunksDirty(int2 chunkCoordinate)
        {
            if (!_chunkStore.TryGetChunk(chunkCoordinate, out ChunkProperties chunk) || chunk == null)
            {
                return;
            }

            ChunkProperties.SubChunk[] subChunks = chunk.SubChunks;
            if (subChunks == null)
            {
                return;
            }

            for (int sy = 0; sy < subChunks.Length; sy++)
            {
                if (subChunks[sy] == null)
                {
                    continue;
                }

                subChunks[sy].IsDirty = true;
                subChunks[sy].IsMeshGenerated = false;
            }
        }

        // center 기준 정사각 범위(|dx|<=range, |dz|<=range) 모든 좌표를 채운다.
        private static void FillNeededCoordinates(int2 center, int range, List<int2> output)
        {
            output.Clear();

            for (int dz = -range; dz <= range; dz++)
            {
                for (int dx = -range; dx <= range; dx++)
                {
                    output.Add(new int2(center.x + dx, center.y + dz));
                }
            }
        }

        // oldCenter 기준 범위 밖이 되는 좌표들(=source 후보)을 만든다.
        private void BuildOutsideCoordinates(int2 oldCenter, int range, List<int2> output)
        {
            output.Clear();

            // 현재 데이터 스토어에 있는 것 중에서 oldCenter 범위를 벗어난 것만 모은다.
            // (완전한 "전체 데이터 풀" 관리가 아니라, store에 존재하는 것만 source로 쓴다)
            // - 이동/재사용 로직을 단순화하기 위해, 여기선 (store에 남아있고 범위 밖인 것)을 source로 본다.
            // - store에 어떤 좌표들이 존재하는지 직접 열람 API가 없어서,
            //   1차 구현에선 "ChunkStore가 보유한 좌표"를 기반으로 source를 만든다.
            //   (데이터 스토어와 chunk 스토어를 동일 범위로 운용한다는 전제)
            IReadOnlyCollection<ChunkProperties> chunks = _chunkStore.GetAllChunks();
            foreach (ChunkProperties c in chunks)
            {
                if (c == null)
                {
                    continue;
                }

                int2 coord = c.ChunkCoordinate;
                int dx = math.abs(coord.x - oldCenter.x);
                int dz = math.abs(coord.y - oldCenter.y);

                if (dx > range || dz > range)
                {
                    output.Add(coord);
                }
            }
        }

        // newCenter 기준으로 "새로 필요한 데이터 좌표" 중 현재 chunk 스토어 범위 밖에서 들어온 것들을 만든다.
        private void BuildNewlyNeededCoordinates(int2 newCenter, int range, List<int2> output)
        {
            output.Clear();

            // 필요한 전체 좌표
            FillNeededCoordinates(newCenter, range, _tmpNeeded);

            // old 범위 밖에서 새로 들어온 것만 골라내는 정확 비교를 하려면 oldCenter가 필요하지만,
            // Move 단계에서 output을 "target 후보"로 쓰는 목적이므로
            // 여기서는 단순히 "현재 ChunkStore에 없는 좌표"를 우선 대상으로 잡는다.
            for (int i = 0; i < _tmpNeeded.Count; i++)
            {
                int2 coord = _tmpNeeded[i];

                // 데이터는 chunk 오브젝트가 없어도 존재할 수 있으나,
                // 1차 구현에선 동일하게 운용하므로 ChunkStore 기준으로 판단.
                if (!_chunkStore.ContainsChunk(coord))
                {
                    output.Add(coord);
                }
            }

            // 만약 target이 너무 적으면(풀 크기 유지), 그냥 필요한 좌표 전체로 확장
            // (재사용 Move를 최대한 채우기 위해)
            if (output.Count == 0)
            {
                output.AddRange(_tmpNeeded);
            }
        }

        // newCenter 렌더 범위에서 "Chunk 오브젝트가 새로 필요"한 좌표들을 만든다.
        private void BuildNewlyNeededChunkObjects(int2 newCenter, int range, List<int2> output)
        {
            output.Clear();

            FillNeededCoordinates(newCenter, range, _tmpNeeded);
            for (int i = 0; i < _tmpNeeded.Count; i++)
            {
                int2 coord = _tmpNeeded[i];
                if (!_chunkStore.ContainsChunk(coord))
                {
                    output.Add(coord);
                }
            }
        }

        private void EnsureAndGenerateChunkData(int2 chunkCoordinate)
        {
            _voxelDataStore.EnsureChunkDataExists(chunkCoordinate);

            if (_voxelDataStore.TryGetChunkDensityBuffer(chunkCoordinate, out NativeArray<byte> density))
            {
                GenerateDensity(chunkCoordinate, density);
            }
        }

        private void EnsureAndGenerateChunkColor(int2 chunkCoordinate)
        {
            if (_voxelColorStore == null)
            {
                return;
            }

            _voxelColorStore.EnsureChunkColorDataExists(chunkCoordinate);

            if (_voxelColorStore.TryGetChunkColorBuffer(chunkCoordinate, out NativeArray<Color32> colors))
            {
                GenerateColor(chunkCoordinate, colors);
            }
        }

        // ----- 간이 지형 생성(1차용) -----

        // density 규칙(1차 고정):
        // - 0 = 공기, 255 = 고체
        // - (x,z)별 높이 h를 만들고, y <= h 이면 255, 아니면 0
        private void GenerateDensity(int2 chunkCoordinate, NativeArray<byte> density)
        {
            int sx = WorldSettings.ChunkSize.x + 1; // 17
            int sy = WorldSettings.ChunkSize.y + 1; // 257
            int sz = WorldSettings.ChunkSize.z + 1; // 17

            int baseWorldX = chunkCoordinate.x * WorldSettings.ChunkSize.x;
            int baseWorldZ = chunkCoordinate.y * WorldSettings.ChunkSize.z;

            for (int y = 0; y < sy; y++)
            {
                for (int z = 0; z < sz; z++)
                {
                    int wz = baseWorldZ + z;
                    for (int x = 0; x < sx; x++)
                    {
                        int wx = baseWorldX + x;

                        int h = GetHeight(wx, wz);
                        byte v = (y <= h) ? (byte)255 : (byte)0;

                        density[GetIndex(x, y, z, sx, sy, sz)] = v;
                    }
                }
            }
        }

        private void GenerateColor(int2 chunkCoordinate, NativeArray<Color32> colors)
        {
            int sx = WorldSettings.ChunkSize.x + 1; // 17
            int sy = WorldSettings.ChunkSize.y + 1; // 257
            int sz = WorldSettings.ChunkSize.z + 1; // 17

            int baseWorldX = chunkCoordinate.x * WorldSettings.ChunkSize.x;
            int baseWorldZ = chunkCoordinate.y * WorldSettings.ChunkSize.z;

            // 간단 규칙:
            // - 지표면 바로 위/아래를 고려하지 않고, y가 높이 이하인 샘플은 "토양색", 아니면 투명(0)
            // - 실제로는 메셔가 vertex color를 어떻게 쓰는지에 맞춰 조정 필요
            Color32 soil = new Color32(110, 85, 60, 255);
            Color32 empty = new Color32(0, 0, 0, 0);

            for (int y = 0; y < sy; y++)
            {
                for (int z = 0; z < sz; z++)
                {
                    int wz = baseWorldZ + z;
                    for (int x = 0; x < sx; x++)
                    {
                        int wx = baseWorldX + x;

                        int h = GetHeight(wx, wz);
                        Color32 c = (y <= h) ? soil : empty;

                        colors[GetIndex(x, y, z, sx, sy, sz)] = c;
                    }
                }
            }
        }

        private int GetHeight(int worldX, int worldZ)
        {
            float n = Mathf.PerlinNoise(worldX * _noiseScale, worldZ * _noiseScale);
            int h = _baseHeight + Mathf.RoundToInt((n - 0.5f) * 2f * _heightVariation);
            return Mathf.Clamp(h, 0, WorldSettings.ChunkSize.y - 1);
        }

        private static int GetIndex(int x, int y, int z, int sx, int sy, int sz)
        {
            // VoxelDataStore/VoxelColorStore와 동일한 메모리 레이아웃:
            // index = (y * sz + z) * sx + x
            return (y * sz + z) * sx + x;
        }
    }
}