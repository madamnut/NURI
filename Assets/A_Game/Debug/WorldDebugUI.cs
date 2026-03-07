using UnityEngine;

/// <summary>
/// 월드 상태를 간단히 화면에 표시하는 디버그 UI이다.
/// </summary>
public sealed class WorldDebugUI : MonoBehaviour
{
    [SerializeField] private WorldSystem _worldSystem;
    [SerializeField] private bool _showOnGui = true;

    private void OnGUI()
    {
        if (!_showOnGui || _worldSystem == null)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10f, 10f, 300f, 120f), GUI.skin.box);
        GUILayout.Label($"Loaded Chunks: {_worldSystem.LoadedChunkCount}");
        GUILayout.Label($"World Generated: {_worldSystem.HasGeneratedWorld}");
        GUILayout.EndArea();
    }
}
