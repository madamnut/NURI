using UnityEngine;

/// <summary>
/// 청크 하나의 시각적 표현을 담당하는 루트 컴포넌트이다.
/// </summary>
public sealed class ChunkView : MonoBehaviour
{
    [SerializeField] private SubChunkView[] _subChunks = new SubChunkView[WorldConstants.SubChunkCount];

    public SubChunkView GetSubChunk(int index)
    {
        if ((uint)index >= (uint)_subChunks.Length)
        {
            return null;
        }

        return _subChunks[index];
    }

    public void SetChunkCoord(ChunkCoord coord)
    {
        transform.position = WorldMath.ChunkOrigin(coord);
        gameObject.name = $"Chunk_{coord.X}_{coord.Z}";
    }

    public void SetMaterial(Material material)
    {
        EnsureSubChunks();

        for (int i = 0; i < _subChunks.Length; i++)
        {
            _subChunks[i].SetMaterial(material);
        }
    }

    public void ClearAllSubChunks()
    {
        EnsureSubChunks();

        for (int i = 0; i < _subChunks.Length; i++)
        {
            _subChunks[i].ClearMesh();
        }
    }

    private void Reset()
    {
        EnsureSubChunks();
    }

    private void Awake()
    {
        EnsureSubChunks();
    }

    private void OnValidate()
    {
        if (_subChunks == null || _subChunks.Length != WorldConstants.SubChunkCount)
        {
            _subChunks = new SubChunkView[WorldConstants.SubChunkCount];
        }
    }

    private void EnsureSubChunks()
    {
        if (_subChunks == null || _subChunks.Length != WorldConstants.SubChunkCount)
        {
            _subChunks = new SubChunkView[WorldConstants.SubChunkCount];
        }

        for (int i = 0; i < WorldConstants.SubChunkCount; i++)
        {
            if (_subChunks[i] == null)
            {
                Transform child = transform.Find($"SubChunk_{i:00}");
                if (child == null)
                {
                    GameObject go = new GameObject($"SubChunk_{i:00}");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(0f, i * WorldConstants.SubChunkSize, 0f);
                    child = go.transform;
                }

                SubChunkView view = child.GetComponent<SubChunkView>();
                if (view == null)
                {
                    view = child.gameObject.AddComponent<SubChunkView>();
                }

                _subChunks[i] = view;
            }
        }
    }
}
