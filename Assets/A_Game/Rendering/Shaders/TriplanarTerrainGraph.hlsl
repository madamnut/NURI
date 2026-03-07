#ifndef A_GAME_TRIPLANAR_TERRAIN_GRAPH_INCLUDED
#define A_GAME_TRIPLANAR_TERRAIN_GRAPH_INCLUDED

/// <summary>
/// Shader Graph Custom Function에서 사용할 트라이플래너 가중치 계산 함수이다.
/// 월드 노멀의 절대값을 기반으로 세 축의 혼합 비율을 만든다.
/// </summary>
float3 AGame_GetTriplanarWeights(float3 normalWS, float blendSharpness)
{
    float3 weights = abs(normalize(normalWS));
    weights = pow(weights, max(blendSharpness, 0.0001));
    return weights / max(weights.x + weights.y + weights.z, 0.0001);
}

/// <summary>
/// 일반 노말맵 샘플을 tangent-space 노말로 복원한다.
/// </summary>
float3 AGame_UnpackNormal(float4 packedNormal, float strength)
{
    float3 normalTS;
    normalTS.xy = (packedNormal.xy * 2.0 - 1.0) * strength;
    normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
    return normalize(normalTS);
}

/// <summary>
/// Shader Graph Lit에서 쓸 트라이플래너 PBR 샘플링 함수이다.
///
/// 입력:
/// - PositionWS: 월드 위치
/// - GeometryNormalWS: 기하학 월드 노멀
/// - BaseMap / NormalMap / RoughnessMap: 각 텍스처 프로퍼티
/// - BaseColor: 색 보정
/// - NormalStrength / RoughnessStrength / Tiling / BlendSharpness: 제어 값
///
/// 출력:
/// - Albedo: Base Color 블록에 연결
/// - WorldNormal: Normal(World) 블록에 연결
/// - Smoothness: Smoothness 블록에 연결
/// </summary>
void TriplanarPBR_float(
    float3 PositionWS,
    float3 GeometryNormalWS,
    UnityTexture2D BaseMap,
    float4 BaseColor,
    UnityTexture2D NormalMap,
    float NormalStrength,
    UnityTexture2D RoughnessMap,
    float RoughnessStrength,
    float Tiling,
    float BlendSharpness,
    out float3 Albedo,
    out float3 WorldNormal,
    out float Smoothness)
{
    float3 geometryNormalWS = normalize(GeometryNormalWS);
    float3 weights = AGame_GetTriplanarWeights(geometryNormalWS, BlendSharpness);

    float2 uvX = PositionWS.zy * Tiling;
    float2 uvY = PositionWS.xz * Tiling;
    float2 uvZ = PositionWS.xy * Tiling;

    uvX = BaseMap.GetTransformedUV(uvX);
    uvY = BaseMap.GetTransformedUV(uvY);
    uvZ = BaseMap.GetTransformedUV(uvZ);

    float4 albedoX = BaseMap.Sample(BaseMap.samplerstate, uvX);
    float4 albedoY = BaseMap.Sample(BaseMap.samplerstate, uvY);
    float4 albedoZ = BaseMap.Sample(BaseMap.samplerstate, uvZ);
    float4 albedo = albedoX * weights.x + albedoY * weights.y + albedoZ * weights.z;
    Albedo = albedo.rgb * BaseColor.rgb;

    float3 normalTSX = AGame_UnpackNormal(NormalMap.Sample(NormalMap.samplerstate, NormalMap.GetTransformedUV(uvX)), NormalStrength);
    float3 normalTSY = AGame_UnpackNormal(NormalMap.Sample(NormalMap.samplerstate, NormalMap.GetTransformedUV(uvY)), NormalStrength);
    float3 normalTSZ = AGame_UnpackNormal(NormalMap.Sample(NormalMap.samplerstate, NormalMap.GetTransformedUV(uvZ)), NormalStrength);

    float3 normalWSX = float3(normalTSX.z, normalTSX.y, normalTSX.x);
    float3 normalWSY = float3(normalTSY.x, normalTSY.z, normalTSY.y);
    float3 normalWSZ = float3(normalTSZ.x, normalTSZ.y, normalTSZ.z);

    float3 blendedNormal = normalWSX * weights.x + normalWSY * weights.y + normalWSZ * weights.z;
    WorldNormal = normalize(lerp(geometryNormalWS, normalize(blendedNormal), saturate(NormalStrength)));

    float roughnessX = RoughnessMap.Sample(RoughnessMap.samplerstate, RoughnessMap.GetTransformedUV(uvX)).r;
    float roughnessY = RoughnessMap.Sample(RoughnessMap.samplerstate, RoughnessMap.GetTransformedUV(uvY)).r;
    float roughnessZ = RoughnessMap.Sample(RoughnessMap.samplerstate, RoughnessMap.GetTransformedUV(uvZ)).r;
    float roughness = roughnessX * weights.x + roughnessY * weights.y + roughnessZ * weights.z;

    roughness = lerp(0.5, roughness, RoughnessStrength);
    Smoothness = saturate(1.0 - roughness);
}

#endif
