using Unity.Mathematics;
using UnityEngine;

namespace NURI
{
    // Chunk 루트 오브젝트가 들고 있는 런타임 상태/참조 모음.
    //
    // - Chunk: (cx, cz)로 식별되는 16 x 16 x 256 단위
    // - SubChunk: (cx, cz, sy)로 식별되는 16 x 16 x 16 단위 (sy = 0..15)
    //
    // 규칙:
    // - 메시/콜라이더 생성 및 갱신은 SubChunk 단위로만 한다.
    // - 데이터(밀도/색 등)는 Chunk 단위로 관리한다(별도 스토어가 들고 있음).
    public sealed class ChunkProperties : MonoBehaviour
    {
        [System.Serializable]
        public sealed class SubChunk
        {
            [SerializeField] private int _sy;
            [SerializeField] private GameObject _gameObject;
            [SerializeField] private MeshFilter _meshFilter;
            [SerializeField] private MeshRenderer _meshRenderer;
            [SerializeField] private MeshCollider _meshCollider;

            private bool _isMeshGenerated;
            private bool _isDirty;

            public int Sy => _sy;
            public GameObject GameObject => _gameObject;
            public MeshFilter MeshFilter => _meshFilter;
            public MeshRenderer MeshRenderer => _meshRenderer;
            public MeshCollider MeshCollider => _meshCollider;

            public bool IsMeshGenerated
            {
                get => _isMeshGenerated;
                set => _isMeshGenerated = value;
            }

            public bool IsDirty
            {
                get => _isDirty;
                set => _isDirty = value;
            }

            public void Initialize(int sy, GameObject go)
            {
                _sy = sy;
                _gameObject = go;

                _meshFilter = go != null ? go.GetComponent<MeshFilter>() : null;
                _meshRenderer = go != null ? go.GetComponent<MeshRenderer>() : null;
                _meshCollider = go != null ? go.GetComponent<MeshCollider>() : null;

                _isMeshGenerated = false;
                _isDirty = false;
            }
        }

        [SerializeField] private SubChunk[] _subChunks;

        private int2 _chunkCoordinate;
        private bool _isInitialized;

        public int2 ChunkCoordinate => _chunkCoordinate;
        public bool IsInitialized => _isInitialized;

        // 길이 16을 전제로 한다. (WorldSettings.SubChunkSize.y=16, ChunkSize.y=256)
        public SubChunk[] SubChunks => _subChunks;

        public void Initialize(int2 chunkCoordinate, SubChunk[] subChunks)
        {
            _chunkCoordinate = chunkCoordinate;
            _subChunks = subChunks;
            _isInitialized = true;
        }

        public SubChunk GetSubChunk(int sy)
        {
            if (_subChunks == null || sy < 0 || sy >= _subChunks.Length)
            {
                return null;
            }

            return _subChunks[sy];
        }

        public void MarkDirty(int sy)
        {
            SubChunk sc = GetSubChunk(sy);
            if (sc == null)
            {
                return;
            }

            sc.IsDirty = true;
        }

        public void ClearDirty(int sy)
        {
            SubChunk sc = GetSubChunk(sy);
            if (sc == null)
            {
                return;
            }

            sc.IsDirty = false;
        }

        public bool HasAnyDirtySubChunk()
        {
            if (_subChunks == null)
            {
                return false;
            }

            for (int i = 0; i < _subChunks.Length; i++)
            {
                if (_subChunks[i] != null && _subChunks[i].IsDirty)
                {
                    return true;
                }
            }

            return false;
        }
    }
}