using System;

/// <summary>
/// 청크 지형 생성에 필요한 파라미터를 모아 둔 설정 구조체이다.
///
/// 현재 지형은 XZ 평면에서 노이즈로 높이를 구한 뒤,
/// 각 샘플 Y가 그 높이보다 얼마나 아래 또는 위에 있는지를 이용해 density를 계산한다.
///
/// 중요한 점은 더 이상 0/255 이진값으로 바로 자르지 않는다는 것이다.
/// SurfaceFade 값을 사용해 표면 근처 density가 서서히 변하도록 만들어
/// 연속적인 scalar field를 유지한다.
/// </summary>
[Serializable]
public struct TerrainGenerationSettings
{
    /// <summary>
    /// XZ 평면에서 노이즈를 샘플링할 때 사용하는 스케일이다.
    /// 값이 작을수록 큰 지형 덩어리가 나오고, 값이 클수록 더 잘게 요철이 생긴다.
    /// </summary>
    public float NoiseScale;

    /// <summary>
    /// 전체 지형의 기본 높이 오프셋이다.
    /// </summary>
    public float BaseHeight;

    /// <summary>
    /// 노이즈가 만들어 낼 높이 변화 폭이다.
    /// </summary>
    public float HeightAmplitude;

    /// <summary>
    /// 표면 근처 density가 얼마나 완만하게 바뀔지를 결정하는 폭이다.
    ///
    /// 값이 작으면 표면이 날카롭고,
    /// 값이 크면 표면 아래와 위가 더 부드럽게 이어진다.
    /// </summary>
    public float SurfaceFade;
}
