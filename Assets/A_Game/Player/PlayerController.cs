using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Player-centered controller for look, movement, camera rig follow, and camera modes.
/// </summary>
public class PlayerController : MonoBehaviour
{
    public enum CameraViewMode
    {
        FirstPerson = 0,
        ThirdPersonBack = 1,
        ThirdPersonFront = 2
    }

    [Header("References")]
    [SerializeField] private Transform _playerRoot;
    [SerializeField] private Transform _head;
    [SerializeField] private Transform _feetPoint;
    [SerializeField] private Transform _firstPersonTarget;
    [SerializeField] private Transform _cameraRig;
    [SerializeField] private Camera _camera;
    [SerializeField] private Rigidbody _rigidbody;
    [SerializeField] private CapsuleCollider _capsuleCollider;
    [SerializeField] private Renderer[] _firstPersonShadowOnlyRenderers;

    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 10f;
    [SerializeField] private float _flySpeedMultiplier = 4f;
    [SerializeField] private float _sprintMultiplier = 4f;
    [SerializeField] private float _jumpVelocity = 6f;
    [SerializeField] private float _flyToggleTapInterval = 0.3f;
    [SerializeField] private LayerMask _groundMask = ~(1 << 3);
    [SerializeField] private float _groundCheckRadius = 0.35f;
    [SerializeField] private float _groundCheckDistance = 0.15f;

    [Header("Look")]
    [SerializeField] private float _mouseSensitivity = 0.15f;
    [SerializeField] private float _cameraMinPitch = -89f;
    [SerializeField] private float _cameraMaxPitch = 89f;
    [SerializeField] private float _headMinPitch = -45f;
    [SerializeField] private float _headMaxPitch = 60f;
    [SerializeField] private bool _lockCursorOnStart = true;

    [Header("Camera Modes")]
    [SerializeField] private CameraViewMode _cameraViewMode = CameraViewMode.FirstPerson;
    [SerializeField] private LayerMask _cameraCollisionMask = ~(1 << 3);
    [SerializeField] private float _thirdPersonBackDistance = 4f;
    [SerializeField] private float _thirdPersonFrontDistance = 4f;
    [SerializeField] private float _cameraCollisionRadius = 0.25f;
    [SerializeField] private float _cameraCollisionPadding = 0.05f;

    private float _yaw;
    private float _cameraPitch;
    private int _defaultCameraCullingMask;
    private ShadowCastingMode[] _originalShadowCastingModes;
    private Vector2 _moveInput;
    private bool _flyAscendHeld;
    private bool _flyDescendHeld;
    private bool _jumpQueued;
    private bool _isGrounded;
    private bool _isFlyMode;
    private float _lastSpaceTapTime = -999f;

    public CameraViewMode CurrentCameraViewMode => _cameraViewMode;
    public string CurrentCameraViewModeLabel => _cameraViewMode switch
    {
        CameraViewMode.FirstPerson => "First",
        CameraViewMode.ThirdPersonBack => "ThirdBack",
        CameraViewMode.ThirdPersonFront => "ThirdFront",
        _ => "Unknown"
    };
    public bool IsFlyMode => _isFlyMode;
    public string CurrentMovementModeLabel => _isFlyMode ? "Flying" : "Ground";
    public Vector3 FeetPosition => _feetPoint != null ? _feetPoint.position : _playerRoot.position;
    public Vector3 InteractionOrigin => _firstPersonTarget != null ? _firstPersonTarget.position : _playerRoot.position;
    public Vector3 InteractionForward => GetLookForward();

    protected virtual void Awake()
    {
        if (!HasRequiredReferences())
        {
            Debug.LogWarning("[PlayerController] Assign all required references in the inspector.", this);
        }
    }

    protected virtual void Start()
    {
        if (!HasRequiredReferences())
        {
            enabled = false;
            return;
        }

        _yaw = _playerRoot.eulerAngles.y;
        _cameraPitch = NormalizeAngle(_cameraRig.eulerAngles.x);
        _cameraPitch = Mathf.Clamp(_cameraPitch, _cameraMinPitch, _cameraMaxPitch);
        _defaultCameraCullingMask = _camera.cullingMask;
        CacheOriginalRendererShadowModes();

        _rigidbody.useGravity = false;
        _rigidbody.linearVelocity = Vector3.zero;
        ApplyCameraVisibilityForMode();
        ApplyMovementMode();

        if (_lockCursorOnStart)
        {
            SetCursorLocked(true);
        }

        ApplyLook();
    }

    protected virtual void Update()
    {
        if (!HasRequiredReferences())
        {
            return;
        }

        HandleCursorToggle();
        HandleCameraModeShortcut();
        CaptureMovementInput();
        HandleMovementModeShortcuts();
        HandleLook();
    }

    protected virtual void FixedUpdate()
    {
        if (!HasRequiredReferences())
        {
            return;
        }

        _isGrounded = ProbeGrounded();
        ApplyMovement();
    }

    protected virtual void LateUpdate()
    {
        if (!HasRequiredReferences())
        {
            return;
        }

        ApplyLook();
    }

    public Ray GetInteractionRay()
    {
        return new Ray(InteractionOrigin, InteractionForward);
    }

    private void HandleCursorToggle()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            SetCursorLocked(!locked);
        }
    }

    private void HandleCameraModeShortcut()
    {
        if (Keyboard.current == null || !Keyboard.current.f5Key.wasPressedThisFrame)
        {
            return;
        }

        _cameraViewMode = _cameraViewMode switch
        {
            CameraViewMode.FirstPerson => CameraViewMode.ThirdPersonBack,
            CameraViewMode.ThirdPersonBack => CameraViewMode.ThirdPersonFront,
            _ => CameraViewMode.FirstPerson
        };

        ApplyCameraVisibilityForMode();
    }

    private void CaptureMovementInput()
    {
        if (Keyboard.current == null)
        {
            _moveInput = Vector2.zero;
            _flyAscendHeld = false;
            _flyDescendHeld = false;
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (Keyboard.current.aKey.isPressed) horizontal -= 1f;
        if (Keyboard.current.dKey.isPressed) horizontal += 1f;
        if (Keyboard.current.sKey.isPressed) vertical -= 1f;
        if (Keyboard.current.wKey.isPressed) vertical += 1f;

        _moveInput = new Vector2(horizontal, vertical);
        if (_moveInput.sqrMagnitude > 1f)
        {
            _moveInput.Normalize();
        }

        _flyAscendHeld = _isFlyMode && Keyboard.current.spaceKey.isPressed;
        _flyDescendHeld = _isFlyMode && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    }

    private void HandleMovementModeShortcuts()
    {
        if (Keyboard.current == null || !Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            return;
        }

        float currentTime = Time.unscaledTime;
        bool isDoubleTap = currentTime - _lastSpaceTapTime <= _flyToggleTapInterval;
        _lastSpaceTapTime = currentTime;

        if (isDoubleTap)
        {
            _jumpQueued = false;
            _isFlyMode = !_isFlyMode;
            ApplyMovementMode();
            return;
        }

        if (!_isFlyMode && _isGrounded)
        {
            _jumpQueued = true;
        }
    }

    private void HandleLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked || Mouse.current == null)
        {
            return;
        }

        Vector2 delta = Mouse.current.delta.ReadValue();
        _yaw += delta.x * _mouseSensitivity;
        _cameraPitch -= delta.y * _mouseSensitivity;
        _cameraPitch = Mathf.Clamp(_cameraPitch, _cameraMinPitch, _cameraMaxPitch);
    }

    private void ApplyMovement()
    {
        float speed = _moveSpeed;
        if (_isFlyMode)
        {
            speed *= _flySpeedMultiplier;
        }

        if (Keyboard.current != null &&
            (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed))
        {
            speed *= _sprintMultiplier;
        }

        if (_isFlyMode)
        {
            Vector3 flyMove =
                _playerRoot.forward * _moveInput.y +
                _playerRoot.right * _moveInput.x +
                (_flyAscendHeld ? Vector3.up : Vector3.zero) +
                (_flyDescendHeld ? Vector3.down : Vector3.zero);

            if (flyMove.sqrMagnitude > 1f)
            {
                flyMove.Normalize();
            }

            _rigidbody.linearVelocity = flyMove * speed;
            return;
        }

        Vector3 desiredHorizontalVelocity =
            (_playerRoot.forward * _moveInput.y + _playerRoot.right * _moveInput.x) * speed;

        Vector3 currentVelocity = _rigidbody.linearVelocity;
        float verticalVelocity = currentVelocity.y;
        if (_jumpQueued && _isGrounded)
        {
            verticalVelocity = _jumpVelocity;
            _jumpQueued = false;
        }

        _rigidbody.linearVelocity = new Vector3(desiredHorizontalVelocity.x, verticalVelocity, desiredHorizontalVelocity.z);
    }

    private void ApplyLook()
    {
        _playerRoot.rotation = Quaternion.Euler(0f, _yaw, 0f);

        float headPitch = Mathf.Clamp(_cameraPitch, _headMinPitch, _headMaxPitch);
        _head.localRotation = Quaternion.Euler(headPitch, 0f, 0f);

        switch (_cameraViewMode)
        {
            case CameraViewMode.FirstPerson:
                _cameraRig.position = _firstPersonTarget.position;
                _cameraRig.rotation = Quaternion.Euler(_cameraPitch, _yaw, 0f);
                break;
            case CameraViewMode.ThirdPersonBack:
                ApplyThirdPersonCamera(-GetLookForward(), _thirdPersonBackDistance, false);
                break;
            case CameraViewMode.ThirdPersonFront:
                ApplyThirdPersonCamera(GetLookForward(), _thirdPersonFrontDistance, true);
                break;
        }
    }

    private void ApplyCameraVisibilityForMode()
    {
        _camera.cullingMask = _defaultCameraCullingMask;
        ApplyPlayerVisibilityForMode();
    }

    private void ApplyPlayerVisibilityForMode()
    {
        if (_firstPersonShadowOnlyRenderers == null || _originalShadowCastingModes == null)
        {
            return;
        }

        bool firstPerson = _cameraViewMode == CameraViewMode.FirstPerson;
        int count = Mathf.Min(_firstPersonShadowOnlyRenderers.Length, _originalShadowCastingModes.Length);
        for (int i = 0; i < count; i++)
        {
            Renderer renderer = _firstPersonShadowOnlyRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.shadowCastingMode = firstPerson
                ? ShadowCastingMode.ShadowsOnly
                : _originalShadowCastingModes[i];
        }
    }

    private void ApplyMovementMode()
    {
        _rigidbody.useGravity = !_isFlyMode;

        if (_isFlyMode)
        {
            _jumpQueued = false;
        }
    }

    private bool ProbeGrounded()
    {
        Bounds bounds = _capsuleCollider.bounds;
        float radius = Mathf.Min(_groundCheckRadius, bounds.extents.x);
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + radius + 0.02f, bounds.center.z);

        return Physics.SphereCast(
            origin,
            radius,
            Vector3.down,
            out _,
            _groundCheckDistance,
            _groundMask,
            QueryTriggerInteraction.Ignore);
    }

    private void ApplyThirdPersonCamera(Vector3 cameraOffsetDirection, float maxDistance, bool lookAtTarget)
    {
        Vector3 pivot = _firstPersonTarget.position;
        Vector3 direction = cameraOffsetDirection.normalized;
        float distance = Mathf.Max(0f, maxDistance);
        Vector3 targetPosition = pivot + direction * distance;

        if (distance > 0.0001f &&
            Physics.SphereCast(
                pivot,
                _cameraCollisionRadius,
                direction,
                out RaycastHit hit,
                distance,
                _cameraCollisionMask,
                QueryTriggerInteraction.Ignore))
        {
            float hitDistance = Mathf.Max(0f, hit.distance - _cameraCollisionPadding);
            targetPosition = pivot + direction * hitDistance;
        }

        _cameraRig.position = targetPosition;
        _cameraRig.rotation = lookAtTarget
            ? Quaternion.LookRotation((pivot - targetPosition).normalized, Vector3.up)
            : Quaternion.LookRotation(GetLookForward(), Vector3.up);
    }

    private Vector3 GetLookForward()
    {
        return Quaternion.Euler(_cameraPitch, _yaw, 0f) * Vector3.forward;
    }

    private void CacheOriginalRendererShadowModes()
    {
        if (_firstPersonShadowOnlyRenderers == null)
        {
            _originalShadowCastingModes = System.Array.Empty<ShadowCastingMode>();
            return;
        }

        _originalShadowCastingModes = new ShadowCastingMode[_firstPersonShadowOnlyRenderers.Length];
        for (int i = 0; i < _firstPersonShadowOnlyRenderers.Length; i++)
        {
            Renderer renderer = _firstPersonShadowOnlyRenderers[i];
            _originalShadowCastingModes[i] = renderer != null
                ? renderer.shadowCastingMode
                : ShadowCastingMode.On;
        }
    }

    private bool HasRequiredReferences()
    {
        return _playerRoot != null &&
               _head != null &&
               _feetPoint != null &&
               _firstPersonTarget != null &&
               _cameraRig != null &&
               _camera != null &&
               _rigidbody != null &&
               _capsuleCollider != null;
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f)
        {
            angle -= 360f;
        }

        while (angle < -180f)
        {
            angle += 360f;
        }

        return angle;
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
