using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NURI
{
    // 플레이어 입력으로 지형(밀도/색)을 수정한다.
    //
    // 규칙:
    // - 복셀 크기 = 1 유닛
    // - 월드 샘플 좌표는 정수 그리드
    // - 밀도: 0(공기) ~ 255(고체)
    // - Chunk/SubChunk Dirty 마킹은 VoxelDataStore/VoxelColorStore가 처리한다.
    public sealed class TerrainDeformer : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private Camera _camera;
        [SerializeField] private VoxelDataStore _voxelDataStore;
        [SerializeField] private VoxelColorStore _voxelColorStore;

        [Header("레이캐스트")]
        [SerializeField] private float _maxDistance = 120f;
        [SerializeField] private LayerMask _terrainLayerMask = ~0;

        [Header("브러시")]
        [SerializeField] private float _radius = 3.5f;
        [SerializeField] private float _strengthPerSecond = 160f; // 1초에 밀도 160 변화(대략)
        [SerializeField] private bool _invertMouseButtons = false;

        [Header("페인트")]
        [SerializeField] private Color32 _paintColor = new Color32(120, 200, 120, 255);

        private void Awake()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }
        }

        private void Update()
        {
            if (_camera == null || _voxelDataStore == null)
            {
                return;
            }

            bool lmb = Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool rmb = Mouse.current != null && Mouse.current.rightButton.isPressed;
            bool mmb = Mouse.current != null && Mouse.current.middleButton.isPressed;

            if (!lmb && !rmb && !mmb)
            {
                return;
            }

            if (!TryRaycast(out RaycastHit hit))
            {
                return;
            }

            int3 center = CoordinateUtilities.WorldToVoxelPosition(hit.point);

            if (mmb)
            {
                PaintSphere(center);
                return;
            }

            bool addSolid = _invertMouseButtons ? rmb : lmb;
            bool removeSolid = _invertMouseButtons ? lmb : rmb;

            if (addSolid)
            {
                DeformSphere(center, +1);
            }
            else if (removeSolid)
            {
                DeformSphere(center, -1);
            }
        }

        private bool TryRaycast(out RaycastHit hit)
        {
            Ray ray = _camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            return Physics.Raycast(ray, out hit, _maxDistance, _terrainLayerMask, QueryTriggerInteraction.Ignore);
        }

        private void DeformSphere(int3 center, int sign)
        {
            float radius = math.max(0.1f, _radius);
            int rCeil = Mathf.CeilToInt(radius);

            // 샘플 좌표 bounds (max는 제외이므로 +1 필요)
            var bounds = new BoundsInt(
                center.x - rCeil,
                center.y - rCeil,
                center.z - rCeil,
                rCeil * 2 + 1,
                rCeil * 2 + 1,
                rCeil * 2 + 1
            );

            float delta = _strengthPerSecond * Time.deltaTime * sign;

            _voxelDataStore.SetDensityCustom(bounds, (p, oldVal) =>
            {
                float dx = p.x - center.x;
                float dy = p.y - center.y;
                float dz = p.z - center.z;

                float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist > radius)
                {
                    return oldVal;
                }

                // 중심에 가까울수록 강하게(0~1)
                float t = 1f - (dist / radius);
                float change = delta * t;

                int next = Mathf.RoundToInt(oldVal + change);
                if (next < 0) next = 0;
                if (next > 255) next = 255;
                return (byte)next;
            });
        }

        private void PaintSphere(int3 center)
        {
            if (_voxelColorStore == null)
            {
                return;
            }

            float radius = math.max(0.1f, _radius);
            int rCeil = Mathf.CeilToInt(radius);

            var bounds = new BoundsInt(
                center.x - rCeil,
                center.y - rCeil,
                center.z - rCeil,
                rCeil * 2 + 1,
                rCeil * 2 + 1,
                rCeil * 2 + 1
            );

            Color32 paint = _paintColor;

            _voxelColorStore.SetColorCustom(bounds, (p, oldColor) =>
            {
                float dx = p.x - center.x;
                float dy = p.y - center.y;
                float dz = p.z - center.z;

                float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist > radius)
                {
                    return oldColor;
                }

                return paint;
            });
        }
    }
}