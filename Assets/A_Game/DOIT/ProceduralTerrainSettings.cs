using UnityEngine;

namespace NURI
{
    // Procedural 지형 생성 파라미터 모음.
    //
    // 주의:
    // - Chunk/SubChunk 크기는 WorldSettings의 고정 법칙을 따른다.
    // - 여기 값들은 "지형 모양"만 바꾸며, 좌표 규칙/크기는 바꾸지 않는다.
    [CreateAssetMenu(menuName = "NURI/Terrain/ProceduralTerrainSettings", fileName = "ProceduralTerrainSettings")]
    public sealed class ProceduralTerrainSettings : ScriptableObject
    {
        [Header("높이(1차 고정 규칙: 높이맵 기반)")]
        [SerializeField] private int _baseHeight = 64;        // 0..255
        [SerializeField] private int _heightVariation = 24;   // >=0
        [SerializeField] private float _noiseScale = 0.02f;

        [Header("색(1차 고정 규칙)")]
        [SerializeField] private Color32 _solidColor = new Color32(110, 85, 60, 255);
        [SerializeField] private Color32 _emptyColor = new Color32(0, 0, 0, 0);

        public int BaseHeight => _baseHeight;
        public int HeightVariation => _heightVariation;
        public float NoiseScale => _noiseScale;

        public Color32 SolidColor => _solidColor;
        public Color32 EmptyColor => _emptyColor;

        private void OnValidate()
        {
            _baseHeight = Mathf.Clamp(_baseHeight, 0, WorldSettings.ChunkSize.y - 1);
            _heightVariation = Mathf.Max(0, _heightVariation);
            _noiseScale = Mathf.Max(0.00001f, _noiseScale);
        }
    }
}