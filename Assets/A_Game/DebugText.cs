// 교체 범위: Assets/Scripts/Temp/DebugText.cs (파일 전체)

using UnityEngine;
using TMPro;

public sealed class DebugText : MonoBehaviour
{
    [Header("대상 TMP Text")]
    [SerializeField] private TMP_Text _text;

    // Update에서 실제 프레임을 카운트하고,
    // FixedUpdate에서 표시만 갱신한다.
    private int _updateFrameCount;
    private float _updateAccumulatedTime;
    private float _lastFps;

    private void Awake()
    {
        // vSync 비활성
        QualitySettings.vSyncCount = 0;
        // 프레임 제한 해제
        Application.targetFrameRate = -1;
    }

    private void Update()
    {
        // 실제 렌더 프레임 기준 FPS 측정
        _updateFrameCount++;
        _updateAccumulatedTime += Time.unscaledDeltaTime;

        // 0.25초 이상 누적되면 FPS 갱신(너무 자주 흔들리지 않게)
        if (_updateAccumulatedTime >= 0.25f)
        {
            _lastFps = _updateFrameCount / _updateAccumulatedTime;
            _updateFrameCount = 0;
            _updateAccumulatedTime = 0f;
        }
    }

    private void FixedUpdate()
    {
        // FixedUpdate마다 UI만 업데이트
        if (_text != null)
            _text.text = $"FPS: {_lastFps:F1}";
    }
}