using UnityEngine;

/// <summary>
/// 게임 씬에서 월드 생성을 시작하는 부트스트랩 컴포넌트이다.
/// </summary>
public sealed class WorldBootstrap : MonoBehaviour
{
    [SerializeField] private WorldSystem _worldSystem;
    [SerializeField] private bool _generateOnStart = true;

    private void Start()
    {
        if (_generateOnStart && _worldSystem != null)
        {
            _worldSystem.GenerateInitialWorld();
        }
    }
}
