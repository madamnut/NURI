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
    private const byte DirtMaterialId = TerrainDensityUtility.DirtMaterialId;
    private const byte RockMaterialId = TerrainDensityUtility.RockMaterialId;

    [Header("참조")]
    [SerializeField] private WorldSystem _worldSystem;
    [SerializeField] private PlayerController _playerController;

    [Header("브러시 설정")]
    [SerializeField] private bool _brushEnabled = false;
    [SerializeField] private float _editSpeedPerSecond = 40f;

    [Header("레이캐스트 설정")]
    [SerializeField] private float _maxRayDistance = 500f;
    [SerializeField] private LayerMask _hitMask = ~0;
    [SerializeField] private bool _debugRaycast = false;

    private float _editAccumulator;
    private float _nextDebugLogTime;
    private byte _selectedMaterialId = DirtMaterialId;

    public float MaxRayDistance => _maxRayDistance;
    public LayerMask HitMask => _hitMask;
    public byte SelectedMaterialId => _selectedMaterialId;
    public bool BrushEnabled => _brushEnabled;
    public float EditSpeedPerSecond => _editSpeedPerSecond;

    public void ToggleBrushEnabled()
    {
        _brushEnabled = !_brushEnabled;
        _editAccumulator = 0f;
    }

    private void Awake()
    {
        if (_worldSystem == null)
        {
            _worldSystem = FindFirstObjectByType<WorldSystem>();
        }
    }

    private void Update()
    {
        HandleEditorShortcuts();
        HandleContinuousEdit();
    }

    private void HandleEditorShortcuts()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.bKey.wasPressedThisFrame)
        {
            ToggleBrushEnabled();
        }
        else if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            _selectedMaterialId = DirtMaterialId;
        }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            _selectedMaterialId = RockMaterialId;
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

        if (!_brushEnabled)
        {
            _editAccumulator = 0f;
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

        if (!TryGetTargetCell(out WorldSystem.CellRaycastHit hit))
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
        if (sign < 0)
        {
            _worldSystem.ApplyCellEdit(hit.Cell, -delta, TerrainDensityUtility.AirMaterialId);
            return;
        }

        Vector3Int targetCell = hit.Cell;
        bool shouldPlaceOnAdjacentCell =
            hit.HasPreviousCell &&
            (hit.MaterialId != _selectedMaterialId || hit.Amount >= byte.MaxValue);

        if (shouldPlaceOnAdjacentCell)
        {
            targetCell = hit.PreviousCell;
        }

        _worldSystem.ApplyCellEdit(targetCell, delta, _selectedMaterialId);
    }

    /// <summary>
    /// 카메라 화면 정중앙에서 레이를 쏴 현재 조준 중인 표면을 찾는다.
    /// </summary>
    private bool TryGetTargetCell(out WorldSystem.CellRaycastHit hit)
    {
        hit = default;

        if (_playerController == null)
        {
            return false;
        }

        Ray ray = _playerController.GetInteractionRay();

        if (_debugRaycast)
        {
            Debug.DrawRay(ray.origin, ray.direction * _maxRayDistance, Color.red, 0f, false);
        }

        bool didHit = _worldSystem != null && _worldSystem.TryRaycastSolidCell(ray, _maxRayDistance, out hit);

        if (_debugRaycast && Time.unscaledTime >= _nextDebugLogTime)
        {
            _nextDebugLogTime = Time.unscaledTime + 0.25f;

            if (didHit)
            {
                Debug.Log($"[VoxelEdit] Cell: {hit.Cell}, Prev: {(hit.HasPreviousCell ? hit.PreviousCell.ToString() : "None")}, Id: {hit.MaterialId}, Amount: {hit.Amount}");
            }
            else
            {
                Debug.Log("[VoxelEdit] Raycast did not hit any non-air cell.");
            }
        }

        return didHit;
    }
}
