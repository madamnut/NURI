using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 개발용 자유 비행 카메라 컨트롤러이다.
///
/// 이 스크립트의 목적은 플레이어 캐릭터를 구현하는 것이 아니라,
/// 월드 생성과 편집 기능을 빠르게 테스트할 수 있는 관찰/편집 카메라를 제공하는 것이다.
///
/// 조작:
/// - W/A/S/D : 수평 이동
/// - Space   : 상승
/// - LeftCtrl: 하강
/// - Shift   : 가속
/// - 마우스 : 시점 회전
/// - Escape  : 커서 잠금 토글
/// </summary>
public sealed class FreeFlyCameraController : MonoBehaviour
{
    [Header("이동 속도")]
    [SerializeField] private float _moveSpeed = 10f;
    [SerializeField] private float _sprintMultiplier = 4f;

    [Header("시점 회전")]
    [SerializeField] private float _mouseSensitivity = 0.15f;
    [SerializeField] private float _minPitch = -89f;
    [SerializeField] private float _maxPitch = 89f;
    [SerializeField] private bool _lockCursorOnStart = true;

    private float _yaw;
    private float _pitch;

    private void Start()
    {
        Vector3 euler = transform.rotation.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x;

        if (_lockCursorOnStart)
        {
            SetCursorLocked(true);
        }
    }

    private void Update()
    {
        HandleCursorToggle();
        HandleLook();
        HandleMove();
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

    private void HandleLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked || Mouse.current == null)
        {
            return;
        }

        Vector2 delta = Mouse.current.delta.ReadValue();
        _yaw += delta.x * _mouseSensitivity;
        _pitch -= delta.y * _mouseSensitivity;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void HandleMove()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        float speed = _moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed)
        {
            speed *= _sprintMultiplier;
        }

        Vector3 move = Vector3.zero;

        if (Keyboard.current.wKey.isPressed) move += transform.forward;
        if (Keyboard.current.sKey.isPressed) move -= transform.forward;
        if (Keyboard.current.dKey.isPressed) move += transform.right;
        if (Keyboard.current.aKey.isPressed) move -= transform.right;
        if (Keyboard.current.spaceKey.isPressed) move += Vector3.up;
        if (Keyboard.current.leftCtrlKey.isPressed) move += Vector3.down;

        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        transform.position += move * speed * Time.deltaTime;
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
