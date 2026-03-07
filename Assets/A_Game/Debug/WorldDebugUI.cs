using TMPro;
using UnityEngine;

/// <summary>
/// 디버그 정보를 하나의 텍스트 컴포넌트에 모아서 표시하는 UI이다.
///
/// 현재는 다음 두 줄만 출력한다.
/// FPS: nnn
/// LoadedChunks: m
///
/// FPS는 매 프레임 즉시 갱신하지 않고, 짧은 구간 동안 누적한 평균값을 사용한다.
/// 이렇게 하면 값이 너무 심하게 출렁이지 않아 읽기 쉬워진다.
/// </summary>
public sealed class WorldDebugUI : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private WorldSystem _worldSystem;
    [SerializeField] private TMP_Text _targetText;

    [Header("표시 설정")]
    [SerializeField] private float _refreshInterval = 0.25f;

    private int _frameCount;
    private float _elapsedTime;
    private int _currentFps;

    private void Awake()
    {
        // FPS 측정이 프레임 제한에 막히지 않도록 vSync와 targetFrameRate 제한을 해제한다.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
    }

    private void OnEnable()
    {
        _frameCount = 0;
        _elapsedTime = 0f;
        _currentFps = 0;
        RefreshText();
    }

    private void Update()
    {
        if (_targetText == null)
        {
            return;
        }

        _frameCount++;
        _elapsedTime += Time.unscaledDeltaTime;

        if (_elapsedTime >= Mathf.Max(0.01f, _refreshInterval))
        {
            _currentFps = Mathf.RoundToInt(_frameCount / _elapsedTime);
            _frameCount = 0;
            _elapsedTime = 0f;
            RefreshText();
        }
    }

    /// <summary>
    /// 현재 디버그 문자열을 만들어 텍스트 컴포넌트에 반영한다.
    /// </summary>
    private void RefreshText()
    {
        if (_targetText == null)
        {
            return;
        }

        int loadedChunks = _worldSystem != null ? _worldSystem.LoadedChunkCount : 0;
        _targetText.text = $"FPS: {_currentFps}\nLoadedChunks: {loadedChunks}";
    }
}
