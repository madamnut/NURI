// 교체 범위: Assets/A_Game/TempCameraController.cs (파일 전체)

using UnityEngine;
using UnityEngine.InputSystem;

public sealed class TempCameraController : MonoBehaviour
{
    [Header("이동")]
    [SerializeField] private float _moveSpeed = 10f;
    [SerializeField] private float _sprintMultiplier = 4f;
    [SerializeField] private float _verticalSpeed = 10f;

    [Header("마우스 보기")]
    [SerializeField] private float _mouseSensitivity = 0.15f; // InputSystem은 델타가 커서 낮게 시작
    [SerializeField] private bool _lockCursorOnStart = true;

    [Header("피치 제한")]
    [SerializeField] private float _minPitch = -89f;
    [SerializeField] private float _maxPitch = 89f;

    private float _yaw;
    private float _pitch;

    private void Start()
    {
        Vector3 e = transform.rotation.eulerAngles;
        _yaw = e.y;
        _pitch = e.x;

        if (_lockCursorOnStart)
            SetCursorLocked(true);
    }

    private void Update()
    {
        HandleCursorToggle();
        HandleLook();
        HandleMove();
    }

    private void HandleCursorToggle()
    {
        // ESC로 커서 잠금 해제/재잠금 토글
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            SetCursorLocked(!locked);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void HandleLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        if (Mouse.current == null)
            return;

        // 마우스 델타(픽셀 단위 비슷). 감도는 인스펙터에서 조절.
        Vector2 delta = Mouse.current.delta.ReadValue();

        _yaw += delta.x * _mouseSensitivity;
        _pitch -= delta.y * _mouseSensitivity;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void HandleMove()
    {
        if (Keyboard.current == null)
            return;

        float speed = _moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed) speed *= _sprintMultiplier;

        float h = 0f;
        float v = 0f;

        if (Keyboard.current.aKey.isPressed) h -= 1f;
        if (Keyboard.current.dKey.isPressed) h += 1f;
        if (Keyboard.current.wKey.isPressed) v += 1f;
        if (Keyboard.current.sKey.isPressed) v -= 1f;

        Vector3 move = (transform.right * h + transform.forward * v);

        // Q/E: 수직 이동(다운/업)
        if (Keyboard.current.eKey.isPressed) move += Vector3.up;
        if (Keyboard.current.qKey.isPressed) move += Vector3.down;

        if (move.sqrMagnitude > 1f) move.Normalize();

        Vector3 horizontalMove = Vector3.ProjectOnPlane(move, Vector3.up) * speed;
        Vector3 verticalMove = Vector3.Project(move, Vector3.up) * _verticalSpeed;

        transform.position += (horizontalMove + verticalMove) * Time.deltaTime;
    }
}