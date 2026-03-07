#ifndef A_GAME_TRIPLANAR_TERRAIN_GRAPH_INCLUDED
#define A_GAME_TRIPLANAR_TERRAIN_GRAPH_INCLUDED

/// <summary>
/// 월드 노말을 기준으로 트라이플래너 가중치를 계산한다.
/// 노말의 절대값을 사용하므로 표면이 어느 축을 더 많이 바라보는지에 따라
/// X / Y / Z 투영 텍스처의 비율이 결정된다.
/// </summary>
float3 AGame_GetTriplanarWeights(float3 normalWS, float blendSharpness)
{
    float3 weights = abs(normalize(normalWS));
    weights = pow(weights, max(blendSharpness, 0.0001));
    return weights / max(weights.x + weights.y + weights.z, 0.0001);
}

/// <summary>
/// 일반 노말맵 샘플을 tangent-space 노말로 복원한다.
/// Shader Graph 쪽에서 노말 강도를 직접 조절할 수 있도록 XY에 강도를 곱한다.
/// </summary>
float3 AGame_UnpackNormal(float4 packedNormal, float strength)
{
    float3 normalTS;
    normalTS.xy = (packedNormal.xy * 2.0 - 1.0) * strength;
    normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
    return normalize(normalTS);
}

/// <summary>
/// 트라이플래너 각 축 평면에 대해 간단한 parallax offset을 계산한다.
/// 실제 정점을 이동시키지 않고, 시선 방향에 따라 UV만 살짝 미는 방식이다.
/// </summary>
float2 AGame_GetParallaxOffset(float2 planeViewXY, float planeViewZ, float heightSample, float heightStrength)
{
    float safeZ = max(abs(planeViewZ), 0.2);
    float centeredHeight = (heightSample - 0.5) * heightStrength;
    return (planeViewXY / safeZ) * centeredHeight;
}

/// <summary>
/// Shader Graph Lit에서 사용하는 트라이플래너 PBR 샘플 함수다.
///
/// 입력:
/// - PositionWS: 월드 위치
/// - GeometryNormalWS: 기하 표면의 월드 노말
/// - BaseMap / NormalMap / RoughnessMap / AOMap / HeightMap: 텍스처 프로퍼티
/// - BaseColor: 알베도 보정 색상
/// - NormalStrength / RoughnessStrength / AOStrength / HeightStrength:
///   각 보조 맵이 얼마나 강하게 적용될지 제어하는 값
/// - Tiling / BlendSharpness: 트라이플래너 타일링 및 축 블렌딩 제어 값
///
/// 출력:
/// - Albedo: Base Color 블록에 연결
/// - WorldNormal: Normal(World) 블록에 연결
/// - Smoothness: Smoothness 블록에 연결
/// - Occlusion: Occlusion 블록에 연결
/// - ParallaxHeight: 실제 메시를 움직이지 않는 높이 표현용 값
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
    UnityTexture2D AOMap,
    float AOStrength,
    UnityTexture2D HeightMap,
    float HeightStrength,
    float3 ViewDirectionWS,
    out float3 Albedo,
    out float3 WorldNormal,
    out float Smoothness,
    out float Occlusion,
    out float ParallaxHeight)
{
    float3 geometryNormalWS = normalize(GeometryNormalWS);
    float3 weights = AGame_GetTriplanarWeights(geometryNormalWS, BlendSharpness);

    float2 uvX = PositionWS.zy * Tiling;
    float2 uvY = PositionWS.xz * Tiling;
    float2 uvZ = PositionWS.xy * Tiling;

    uvX = BaseMap.GetTransformedUV(uvX);
    uvY = BaseMap.GetTransformedUV(uvY);
    uvZ = BaseMap.GetTransformedUV(uvZ);

    float heightXInitial = HeightMap.Sample(HeightMap.samplerstate, HeightMap.GetTransformedUV(uvX)).r;
    float heightYInitial = HeightMap.Sample(HeightMap.samplerstate, HeightMap.GetTransformedUV(uvY)).r;
    float heightZInitial = HeightMap.Sample(HeightMap.samplerstate, HeightMap.GetTransformedUV(uvZ)).r;

    float3 viewDirWS = normalize(ViewDirectionWS);
    float2 parallaxOffsetX = AGame_GetParallaxOffset(viewDirWS.zy, viewDirWS.x, heightXInitial, HeightStrength);
    float2 parallaxOffsetY = AGame_GetParallaxOffset(viewDirWS.xz, viewDirWS.y, heightYInitial, HeightStrength);
    float2 parallaxOffsetZ = AGame_GetParallaxOffset(viewDirWS.xy, viewDirWS.z, heightZInitial, HeightStrength);

    uvX += parallaxOffsetX;
    uvY += parallaxOffsetY;
    uvZ += parallaxOffsetZ;

    float4 albedoX = BaseMap.Sample(BaseMap.samplerstate, uvX);
    float4 albedoY = BaseMap.Sample(BaseMap.samplerstate, uvY);
    float4 albedoZ = BaseMap.Sample(BaseMap.samplerstate, uvZ);
    float4 albedo = albedoX * weights.x + albedoY * weights.y + albedoZ * weights.z;
    Albedo = albedo.rgb * BaseColor.rgb;

    float3 normalTSX = AGame_UnpackNormal(
        NormalMap.Sample(NormalMap.samplerstate, NormalMap.GetTransformedUV(uvX)),
        NormalStrength);
    float3 normalTSY = AGame_UnpackNormal(
        NormalMap.Sample(NormalMap.samplerstate, NormalMap.GetTransformedUV(uvY)),
        NormalStrength);
    float3 normalTSZ = AGame_UnpackNormal(
        NormalMap.Sample(NormalMap.samplerstate, NormalMap.GetTransformedUV(uvZ)),
        NormalStrength);

    float3 normalWSX = float3(normalTSX.z, normalTSX.y, normalTSX.x);
    float3 normalWSY = float3(normalTSY.x, normalTSY.z, normalTSY.y);
    float3 normalWSZ = float3(normalTSZ.x, normalTSZ.y, normalTSZ.z);

    float3 blendedNormal = normalWSX * weights.x + normalWSY * weights.y + normalWSZ * weights.z;
    WorldNormal = normalize(lerp(geometryNormalWS, normalize(blendedNormal), saturate(NormalStrength)));

    float roughnessX = RoughnessMap.Sample(RoughnessMap.samplerstate, RoughnessMap.GetTransformedUV(uvX)).r;
    float roughnessY = RoughnessMap.Sample(RoughnessMap.samplerstate, RoughnessMap.GetTransformedUV(uvY)).r;
    float roughnessZ = RoughnessMap.Sample(RoughnessMap.samplerstate, RoughnessMap.GetTransformedUV(uvZ)).r;
    float roughness = roughnessX * weights.x + roughnessY * weights.y + roughnessZ * weights.z;
    roughness = lerp(0.5, roughness, saturate(RoughnessStrength));
    Smoothness = saturate(1.0 - roughness);

    float aoX = AOMap.Sample(AOMap.samplerstate, AOMap.GetTransformedUV(uvX)).r;
    float aoY = AOMap.Sample(AOMap.samplerstate, AOMap.GetTransformedUV(uvY)).r;
    float aoZ = AOMap.Sample(AOMap.samplerstate, AOMap.GetTransformedUV(uvZ)).r;
    float ao = aoX * weights.x + aoY * weights.y + aoZ * weights.z;
    Occlusion = lerp(1.0, ao, saturate(AOStrength));

    float heightX = HeightMap.Sample(HeightMap.samplerstate, HeightMap.GetTransformedUV(uvX)).r;
    float heightY = HeightMap.Sample(HeightMap.samplerstate, HeightMap.GetTransformedUV(uvY)).r;
    float heightZ = HeightMap.Sample(HeightMap.samplerstate, HeightMap.GetTransformedUV(uvZ)).r;
    float height = heightX * weights.x + heightY * weights.y + heightZ * weights.z;
    ParallaxHeight = height * HeightStrength;
}

#endif
