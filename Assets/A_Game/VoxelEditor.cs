// Assets/Scripts/World/VoxelEditor.cs
// 교체 범위: 파일 전체

using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public sealed class VoxelEditor : MonoBehaviour
{
    private enum BrushMode
    {
        Ball = 0,
        Beam = 1
    }

    [Header("카메라(비우면 MainCamera)")]
    [SerializeField] private Camera _camera;

    [Header("월드(편집 적용 대상)")]
    [SerializeField] private WorldBootstrap10x10 _world;

    [Header("UI (TMP_Text 2개 할당)")]
    [SerializeField] private TMP_Text _modeText;   // "BrushMode: Ball/Beam"
    [SerializeField] private TMP_Text _paramText;  // "- Radius: n" / "- Speed: n"

    [Header("Ball 파라미터")]
    [SerializeField] private float _radius = 4f;
    [SerializeField] private float _radiusStep = 0.5f;
    [SerializeField] private float _minRadius = 0.5f;
    [SerializeField] private float _maxRadius = 32f;

    [Header("Beam 파라미터")]
    [Tooltip("초당 density(amount) 변화량")]
    [SerializeField] private float _beamSpeed = 30f;
    [SerializeField] private float _beamSpeedStep = 5f;
    [SerializeField] private float _minBeamSpeed = 1f;
    [SerializeField] private float _maxBeamSpeed = 512f;

    [Header("편집 강도(Ball)")]
    [Tooltip("클릭 1회당 density(amount) 변화량")]
    [SerializeField] private int _ballDeltaPerClick = 8;

    [Header("레이캐스트")]
    [SerializeField] private float _maxRayDistance = 500f;
    [SerializeField] private LayerMask _hitMask = ~0;

    private BrushMode _mode = BrushMode.Ball;

    // Beam은 float 누적 후 정수로 반영(프레임별 델타 손실 방지)
    private float _beamAccum;

    private void Awake()
    {
        if (_camera == null) _camera = Camera.main;
        if (_world == null) _world = FindFirstObjectByType<WorldBootstrap10x10>();
    }

    private void Update()
    {
        HandleModeToggle();
        HandleScrollParam();
        HandleEditInput();
        UpdateUI();
    }

    private void HandleModeToggle()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.vKey.wasPressedThisFrame)
        {
            _mode = (_mode == BrushMode.Ball) ? BrushMode.Beam : BrushMode.Ball;
            _beamAccum = 0f;
        }
    }

    private void HandleScrollParam()
    {
        if (Mouse.current == null) return;

        float scrollY = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scrollY) < 0.01f) return;

        float dir = Mathf.Sign(scrollY);

        if (_mode == BrushMode.Ball)
        {
            _radius += dir * _radiusStep;
            _radius = Mathf.Clamp(_radius, _minRadius, _maxRadius);
        }
        else
        {
            _beamSpeed += dir * _beamSpeedStep;
            _beamSpeed = Mathf.Clamp(_beamSpeed, _minBeamSpeed, _maxBeamSpeed);
        }
    }

    private void HandleEditInput()
    {
        if (_world == null) return;
        if (Mouse.current == null) return;

        bool destroyHeld = Mouse.current.leftButton.isPressed;
        bool createHeld = Mouse.current.rightButton.isPressed;

        if (!destroyHeld && !createHeld)
        {
            _beamAccum = 0f;
            return;
        }

        if (!TryGetHitPoint(out Vector3 hitPoint))
            return;

        // 동시에 누르면 좌클릭(파괴) 우선
        int sign = destroyHeld ? -1 : +1;

        if (_mode == BrushMode.Ball)
        {
            // Ball: 클릭 1회만 적용
            bool destroyClick = Mouse.current.leftButton.wasPressedThisFrame;
            bool createClick = Mouse.current.rightButton.wasPressedThisFrame;
            if (!destroyClick && !createClick) return;

            _world.ApplyBallBrush(hitPoint, _radius, sign * _ballDeltaPerClick);
        }
        else
        {
            // Beam: 누르고 있는 동안 초당 amount 변화량을 누적 적용
            float delta = _beamSpeed * Time.unscaledDeltaTime;
            _beamAccum += delta;

            int deltaInt = Mathf.FloorToInt(_beamAccum);
            if (deltaInt == 0) return;

            _beamAccum -= deltaInt;
            _world.ApplyBallBrush(hitPoint, _radius, sign * deltaInt);
        }
    }

    private bool TryGetHitPoint(out Vector3 hitPoint)
    {
        hitPoint = default;

        Camera cam = _camera != null ? _camera : Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance, _hitMask, QueryTriggerInteraction.Ignore))
        {
            hitPoint = hit.point;
            return true;
        }

        return false;
    }

    private void UpdateUI()
    {
        if (_modeText != null)
            _modeText.text = (_mode == BrushMode.Ball) ? "BrushMode: Ball" : "BrushMode: Beam";

        if (_paramText != null)
        {
            if (_mode == BrushMode.Ball)
                _paramText.text = $"- Radius: {_radius:F1}";
            else
                _paramText.text = $"- Speed: {_beamSpeed:F1}";
        }
    }
}