using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 화면 중앙을 기준으로 지형을 생성하거나 파괴하는 편집 컨트롤러이다.
///
/// 카메라 정중앙에서 레이캐스트를 쏘고,
/// 좌클릭 또는 우클릭을 누르고 있는 동안 연속적으로 브러시를 적용한다.
///
/// 편집 속도는 "초당 변화량" 개념으로 계산한다.
/// 이렇게 하면 프레임레이트가 달라도 대체로 비슷한 편집 속도를 유지할 수 있다.
/// </summary>
public sealed class VoxelEditController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Camera _camera;
    [SerializeField] private WorldSystem _worldSystem;

    [Header("브러시 설정")]
    [SerializeField] private float _brushRadius = 3f;
    [SerializeField] private float _editSpeedPerSecond = 40f;

    [Header("레이캐스트 설정")]
    [SerializeField] private float _maxRayDistance = 500f;
    [SerializeField] private LayerMask _hitMask = ~0;
    [SerializeField] private bool _debugRaycast = false;

    private float _editAccumulator;
    private float _nextDebugLogTime;

    private void Awake()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
        }

        if (_worldSystem == null)
        {
            _worldSystem = FindFirstObjectByType<WorldSystem>();
        }
    }

    private void Update()
    {
        HandleRebuildShortcut();
        HandleContinuousEdit();
    }

    /// <summary>
    /// R 키를 누르면 현재 density 데이터를 기준으로 전체 청크 메시를 다시 만든다.
    /// </summary>
    private void HandleRebuildShortcut()
    {
        if (_worldSystem == null || Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            _worldSystem.RebuildAllLoadedChunks(false);
        }
    }

    /// <summary>
    /// 마우스 버튼을 누르고 있는 동안 연속 편집을 처리한다.
    /// 좌클릭은 파괴, 우클릭은 생성이다.
    /// </summary>
    private void HandleContinuousEdit()
    {
        if (_worldSystem == null || Mouse.current == null)
        {
            return;
        }

        bool destroyHeld = Mouse.current.leftButton.isPressed;
        bool createHeld = Mouse.current.rightButton.isPressed;

        if (!destroyHeld && !createHeld)
        {
            _editAccumulator = 0f;
            return;
        }

        int sign = destroyHeld ? -1 : 1;

        if (!TryGetEditHit(out RaycastHit hit))
        {
            _editAccumulator = 0f;
            return;
        }

        _editAccumulator += _editSpeedPerSecond * Time.deltaTime;
        int delta = Mathf.FloorToInt(_editAccumulator);

        if (delta <= 0)
        {
            return;
        }

        _editAccumulator -= delta;
        _worldSystem.ApplyOrientedBrush(hit.point, hit.normal, _brushRadius, sign * delta);
    }

    /// <summary>
    /// 카메라 화면 정중앙에서 레이를 쏴 현재 조준 중인 표면을 찾는다.
    /// </summary>
    private bool TryGetEditHit(out RaycastHit hit)
    {
        hit = default;

        Camera cam = _camera != null ? _camera : Camera.main;
        if (cam == null)
        {
            return false;
        }

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (_debugRaycast)
        {
            Debug.DrawRay(ray.origin, ray.direction * _maxRayDistance, Color.red, 0f, false);
        }

        bool didHit = Physics.Raycast(ray, out hit, _maxRayDistance, _hitMask, QueryTriggerInteraction.Ignore);

        if (_debugRaycast && Time.unscaledTime >= _nextDebugLogTime)
        {
            _nextDebugLogTime = Time.unscaledTime + 0.25f;

            if (didHit)
            {
                Debug.Log($"[VoxelEdit] Hit: {hit.collider.name}, Point: {hit.point}, Normal: {hit.normal}");
            }
            else
            {
                Debug.Log("[VoxelEdit] Raycast did not hit anything.");
            }
        }

        return didHit;
    }
}
